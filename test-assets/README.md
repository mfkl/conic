# Test assets

The deterministic local composition fixture is currently supplied from the
sibling VLCLR checkout as `test_01.mkv`.

| File | Bytes | SHA-256 |
|---|---:|---|
| `test_01.mkv` | 3,748,131 | `C3F37E9DD4740F82019D2E58A9AF58FA318BCC0B4C392FBD831A4FA40EE315A2` |

The media file is intentionally not committed here. CI must fetch or generate
an asset with this digest before native smoke tests run.
