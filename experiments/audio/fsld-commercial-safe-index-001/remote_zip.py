from __future__ import annotations

import binascii
import bz2
import io
import struct
import urllib.request
import zlib
from dataclasses import dataclass


EOCD = b"PK\x05\x06"
ZIP64_LOCATOR = b"PK\x06\x07"
ZIP64_EOCD = b"PK\x06\x06"
CENTRAL = b"PK\x01\x02"
LOCAL = b"PK\x03\x04"


@dataclass(frozen=True)
class Entry:
    name: str
    method: int
    flags: int
    crc32: int
    compressed_size: int
    uncompressed_size: int
    local_offset: int


class RemoteZip:
    def __init__(self, url: str, user_agent: str = "ziro-lab-fsld-index/0.1"):
        self.url = url
        self.user_agent = user_agent
        self.fetched_bytes = 0
        self.range_requests = 0
        self.size = self._discover_size()
        self.entries = self._read_directory()

    def _request(self, start: int, end: int) -> bytes:
        if start < 0 or end < start or end >= self.size:
            raise ValueError(f"invalid_range:{start}-{end}/{self.size}")
        req = urllib.request.Request(
            self.url,
            headers={
                "User-Agent": self.user_agent,
                "Accept": "*/*",
                "Range": f"bytes={start}-{end}",
            },
        )
        with urllib.request.urlopen(req, timeout=90) as response:
            status = getattr(response, "status", None)
            content_range = response.headers.get("Content-Range")
            if status != 206 or not content_range:
                raise RuntimeError(
                    f"range_not_honored:status={status}:content_range={content_range}"
                )
            data = response.read()
        expected = end - start + 1
        if len(data) != expected:
            raise RuntimeError(f"short_range_read:{len(data)}!={expected}")
        self.fetched_bytes += len(data)
        self.range_requests += 1
        return data

    def _discover_size(self) -> int:
        # A one-byte range gives the authoritative total length without pulling the archive.
        req = urllib.request.Request(
            self.url,
            headers={
                "User-Agent": self.user_agent,
                "Accept": "*/*",
                "Range": "bytes=0-0",
            },
        )
        with urllib.request.urlopen(req, timeout=90) as response:
            status = getattr(response, "status", None)
            cr = response.headers.get("Content-Range")
            if status != 206 or not cr or "/" not in cr:
                raise RuntimeError(f"range_size_probe_failed:status={status}:content_range={cr}")
            data = response.read()
        if len(data) != 1:
            raise RuntimeError("range_size_probe_returned_more_than_one_byte")
        self.fetched_bytes += 1
        self.range_requests += 1
        total = int(cr.rsplit("/", 1)[1])
        if total <= 0:
            raise RuntimeError("invalid_remote_size")
        return total

    def _central_location(self) -> tuple[int, int]:
        tail_len = min(self.size, 256 * 1024)
        tail_start = self.size - tail_len
        tail = self._request(tail_start, self.size - 1)
        eocd_rel = tail.rfind(EOCD)
        if eocd_rel < 0 or eocd_rel + 22 > len(tail):
            raise RuntimeError("zip_eocd_not_found")
        eocd = struct.unpack_from("<4s4H2IH", tail, eocd_rel)
        cd_size = eocd[5]
        cd_offset = eocd[6]
        if cd_size != 0xFFFFFFFF and cd_offset != 0xFFFFFFFF:
            return int(cd_offset), int(cd_size)

        locator_rel = tail.rfind(ZIP64_LOCATOR, 0, eocd_rel)
        if locator_rel < 0 or locator_rel + 20 > len(tail):
            raise RuntimeError("zip64_locator_not_found")
        _, _disk, zip64_offset, _disks = struct.unpack_from("<4sIQI", tail, locator_rel)
        raw = self._request(int(zip64_offset), int(zip64_offset) + 55)
        values = struct.unpack_from("<4sQ2H2I4Q", raw, 0)
        if values[0] != ZIP64_EOCD:
            raise RuntimeError("zip64_eocd_bad_signature")
        return int(values[-1]), int(values[-2])

    @staticmethod
    def _zip64_values(extra: bytes, need_uncompressed: bool, need_compressed: bool, need_offset: bool):
        pos = 0
        payload = None
        while pos + 4 <= len(extra):
            tag, size = struct.unpack_from("<HH", extra, pos)
            pos += 4
            block = extra[pos:pos + size]
            pos += size
            if tag == 0x0001:
                payload = block
                break
        if payload is None:
            raise RuntimeError("zip64_extra_missing")
        cursor = 0
        out = {}
        for key, needed in (
            ("uncompressed", need_uncompressed),
            ("compressed", need_compressed),
            ("offset", need_offset),
        ):
            if needed:
                if cursor + 8 > len(payload):
                    raise RuntimeError("zip64_extra_truncated")
                out[key] = struct.unpack_from("<Q", payload, cursor)[0]
                cursor += 8
        return out

    def _read_directory(self) -> dict[str, Entry]:
        cd_offset, cd_size = self._central_location()
        if cd_size <= 0 or cd_size > 64 * 1024 * 1024:
            raise RuntimeError(f"unexpected_central_directory_size:{cd_size}")
        data = self._request(cd_offset, cd_offset + cd_size - 1)
        result: dict[str, Entry] = {}
        pos = 0
        while pos + 46 <= len(data):
            if data[pos:pos + 4] != CENTRAL:
                raise RuntimeError(f"bad_central_signature_at:{pos}")
            vals = struct.unpack_from("<4s6H3I5H2I", data, pos)
            flags = vals[3]
            method = vals[4]
            crc = vals[7]
            comp = vals[8]
            uncomp = vals[9]
            name_len = vals[10]
            extra_len = vals[11]
            comment_len = vals[12]
            local_offset = vals[16]
            start = pos + 46
            name_bytes = data[start:start + name_len]
            extra = data[start + name_len:start + name_len + extra_len]
            encoding = "utf-8" if flags & 0x800 else "cp437"
            name = name_bytes.decode(encoding, errors="strict")

            need_uncomp = uncomp == 0xFFFFFFFF
            need_comp = comp == 0xFFFFFFFF
            need_offset = local_offset == 0xFFFFFFFF
            if need_uncomp or need_comp or need_offset:
                z = self._zip64_values(extra, need_uncomp, need_comp, need_offset)
                uncomp = z.get("uncompressed", uncomp)
                comp = z.get("compressed", comp)
                local_offset = z.get("offset", local_offset)

            result[name] = Entry(
                name=name,
                method=int(method),
                flags=int(flags),
                crc32=int(crc),
                compressed_size=int(comp),
                uncompressed_size=int(uncomp),
                local_offset=int(local_offset),
            )
            pos = start + name_len + extra_len + comment_len
        if pos != len(data):
            # Central-directory digital signatures are uncommon; fail closed instead of guessing.
            raise RuntimeError(f"central_directory_trailing_bytes:{len(data)-pos}")
        return result

    def read(self, name: str, max_uncompressed: int = 64 * 1024 * 1024) -> bytes:
        entry = self.entries[name]
        if entry.uncompressed_size > max_uncompressed:
            raise RuntimeError(f"member_too_large:{name}:{entry.uncompressed_size}")
        raw = self._request(entry.local_offset, entry.local_offset + 29)
        vals = struct.unpack_from("<4s5H3I2H", raw, 0)
        if vals[0] != LOCAL:
            raise RuntimeError(f"bad_local_header:{name}")
        name_len, extra_len = vals[-2], vals[-1]
        data_start = entry.local_offset + 30 + name_len + extra_len
        if entry.compressed_size == 0:
            compressed = b""
        else:
            compressed = self._request(data_start, data_start + entry.compressed_size - 1)

        if entry.method == 0:
            data = compressed
        elif entry.method == 8:
            data = zlib.decompress(compressed, -15)
        elif entry.method == 12:
            data = bz2.decompress(compressed)
        else:
            raise RuntimeError(f"unsupported_zip_method:{entry.method}:{name}")
        if len(data) != entry.uncompressed_size:
            raise RuntimeError(f"member_size_mismatch:{name}")
        if (binascii.crc32(data) & 0xFFFFFFFF) != entry.crc32:
            raise RuntimeError(f"member_crc_mismatch:{name}")
        return data
