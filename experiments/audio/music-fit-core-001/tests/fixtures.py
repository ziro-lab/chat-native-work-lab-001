"""Repository-owned deterministic musical fixtures. Ground truth stays in tests."""
import numpy as np

def phrase(sr=16000, seconds_per_note=0.5, seed=1, notes=None, drums=True):
    rng=np.random.default_rng(seed)
    notes=notes or [48,55,60,64,50,57,62,65,53,60,65,69,55,62,67,71]
    blocks=[]
    for idx,n in enumerate(notes):
        length=round(seconds_per_note*sr); t=np.arange(length)/sr
        f=440*2**((n-69)/12)
        env=np.minimum(1,t/.015)*np.minimum(1,(seconds_per_note-t)/.025)
        pad=(np.sin(2*np.pi*f*t)+.3*np.sin(4*np.pi*f*t)+.15*np.sin(2*np.pi*f*1.5*t))*.12*env
        if drums:
            pad+=.16*np.sin(2*np.pi*65*t)*np.exp(-t*35)
            pad+=rng.standard_normal(length)*.014*np.exp(-t*50)
        blocks.append(pad)
    return np.concatenate(blocks).astype(np.float32)

def song(sr=16000, seed=1, seconds_per_note=0.5, drums=True, cycles=4):
    base=phrase(sr,seconds_per_note,seed,drums=drums)
    rng=np.random.default_rng(seed+100)
    lead=round(2.13*sr); end=round(2.27*sr)
    intro=np.sin(2*np.pi*173*np.arange(lead)/sr)*.04*np.linspace(0,1,lead)
    outro=np.sin(2*np.pi*220*np.arange(end)/sr)*.06*np.linspace(1,0,end)
    body=[]
    for i in range(cycles):
        # Independent gains, weak harmonics and noise without phase/time changes.
        x=base*(.84+.08*(i%3))+rng.standard_normal(len(base))*.0002
        body.append(x.astype(np.float32))
    mono=np.concatenate([intro,*body,outro]).astype(np.float32)
    stereo=np.stack([mono,mono*.91],axis=1)
    return stereo,{'period':len(base),'body_start':lead,'body_end':lead+cycles*len(base),'sr':sr}

def two_sections(sr=16000):
    a=phrase(sr,.5,52)
    b=phrase(sr,.5,68,notes=[59,66,71,74,57,64,69,72,54,61,66,69])
    def varied(base,k):
        rng=np.random.default_rng(440+k)
        return np.tanh(base*(.93+.045*k))+rng.normal(0,.00005,len(base))
    intro=phrase(sr,.5,10,notes=[40,42,44,47])*.4
    outro=phrase(sr,.5,10,notes=[52,55,59,64])*.4*np.linspace(1,0,2*sr)
    x=np.concatenate([intro,*[varied(a,k) for k in range(3)],*[varied(b,k) for k in range(3)],outro]).astype(np.float32)
    return np.stack([x,x*.89],axis=1),[len(a),len(b)]
