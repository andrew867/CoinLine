# Threat model (summary)

| Threat | Mitigation / status |
|--------|---------------------|
| Stolen API key | Rotate keys; network segmentation |
| Raw payload exfiltration via replay export | Confirm gates + audit |
| Unauthorized firmware approve | `RequireAdmin` |
| PCI data in host DB | Out of scope — token refs only — **validate deployment** |
| Operator mistakes on live flash | Default **`AllowLiveFlashing=false`** |

Deep review should pair this with organizational pentest scope.
