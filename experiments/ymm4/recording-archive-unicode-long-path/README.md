# Recording Archive Unicode and long absolute path

## Question

Can YMM4 v4.56.1.0 load/save a normal VideoItem whose absolute media/project paths contain Japanese text, emoji, spaces, brackets, and a long directory segment?

## PASS boundary

PASS requires real-host ContentLength loading plus SaveProject/LoadProjectFile roundtrip with the exact absolute media path preserved.
