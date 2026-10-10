# Image codec fixtures

The small encoded corpus covers distinct format branches and specific regressions.
`expected.json` records independently computed dimensions, lossless pixel digests, representative
lossy pixels and metadata digests.

The test-only generators require Pillow 12.2.0. They write encoded inputs and independently
computed expected pixels into an ignored build directory:

```powershell
python tests/DotCraft.Imaging.Tests/Fixtures/png-fixtures.py
python tests/DotCraft.Imaging.Tests/Fixtures/jpeg-generate.py
python tests/DotCraft.Imaging.Tests/Fixtures/webp-generate.py
```

Copy only a new format branch or reproducible regression into this directory. Compute its
expectations from the independent generator output, never from the managed codec being tested.
