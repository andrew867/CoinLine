# NCC stream chunk fixtures

Chunked incremental decoding is validated in `NccIncrementalStreamDecoderTests` (`Chunked_feed_matches_full_ordered_parse`) using programmatic frames.

Use `fixtures/ncc/valid/clr_sample.*` for end-to-end valid CLR samples; split any `.bin` across arbitrary chunk boundaries when exercising `NccIncrementalStreamDecoder.Feed` — ordered frames must match `NccStreamReader.ReadOrdered` on the concatenated buffer.
