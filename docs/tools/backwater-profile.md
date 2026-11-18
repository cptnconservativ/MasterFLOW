# Backwater / drawdown profile (gradually varied flow)

Water surface along a prismatic channel (rectangular, trapezoidal, triangular) or a partly full circular
pipe, from a control depth: a culvert headwater, pond level or junction (downstream control on a mild
slope), a free overfall (`control depth = critical`), or the upstream end of a steep channel.

| Item | Method |
|---|---|
| Step | Standard step: E₂ = E₁ ∓ S₀Δx ± ½(Sf₁ + Sf₂)Δx, 200 equal steps over the reach |
| Depth at each step | Bisection on the branch of the specific-energy curve for the regime (above yc for subcritical, below for supercritical) |
| Friction slope | Manning, Sf = (n V / (k R^⅔))², averaged over the step |
| Direction | Subcritical (mild, horizontal, adverse): upstream from a downstream control. Supercritical (steep): downstream from an upstream control |
| Profile class | Chow (1959): M, S, C, H, A with zones 1–3 |
| Bank check | Depth against bank depth and the required freeboard (IDCM §4.8 / Table 4-14 for the channel type) |

A control depth that the regime cannot reach (below critical on a mild slope; above critical on a steep
slope) is replaced by critical depth, with a message. When no depth on the regime's branch satisfies the
energy equation, the flow must pass through critical depth (a jump or a drawdown); the profile stops there and
says so, rather than jumping branches. Hydraulic jump location is not computed.

Decision (D-056): standard step with fixed distance steps was chosen over direct step so the output lands at
regular stations along the channel and handles horizontal and adverse slopes (no normal depth). The tests
check it against an independent direct-step integration (within 0.01 ft) and the approach to normal depth.
