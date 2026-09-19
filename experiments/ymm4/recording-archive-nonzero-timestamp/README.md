# Recording Archive non-zero container timestamp

## Question

Does YMM4 v4.56.1.0 accept a VideoItem backed by a valid AV container whose format start_time is non-zero, even though the current Recording Archive candidate rejects such media conservatively?

## PASS boundary

PASS records whether YMM4 loads the media and reports positive content duration. It does not prove that the current stream-copy relink math is safe for non-zero-start containers.
