# Sewer sulfide build-up and corrosion

Tool id `sewer-sulfide` (free, U.S. units). Follows the sulfide concentration reach by reach down a long sanitary
sewer run, including force mains, and turns it into a concrete corrosion rate and a material or lining
recommendation for the Rapid City IDCM design life: §3.12.1 sets a 75-year minimum design life goal for collection
system hardware, and §3.12.8 item 2 asks the engineer to address coatings or other corrosion protection to reach it.
For sewers larger than 24 in., IDCM §3.12.14 item 5 asks for the ASCE MOP 60 sulfide analysis in place of the Z
screening; this tool is that analysis. The Z screening itself (formula as printed, flattest slope, 1 to 105 % of the
peak) stays in `sanitary-network`.

Sources: U.S. EPA, *Design Manual: Odor and Corrosion Control in Sanitary Sewerage Systems and Treatment Plants*
(EPA 625/1-85/018, 1985), Ch. 3; ASCE MOP 69, *Sulfide in Wastewater Collection and Treatment Systems* (1989);
Pomeroy and Parkhurst (1977), *Prog. Water Tech.* 9; Pomeroy (1976) concrete corrosion. The full-pipe form and its M
values are as quoted in WSSC *Sewer Design Guidelines* (2008).

## Input

Keys: `BOD` (mg/L BOD5, 250), `temperature` (°C, 20) or `temperature F`, `pH` (7.0), `pK1` (default from
temperature), `initial sulfide` (mg/L, 0), `lateral sulfide` (mg/L in added flow, 0), `dissolved fraction` (0.8),
`M gravity` (M′, 0.32×10⁻³ m/h), `m loss` (0.64), `M force main` (default by size, below), `k` (0.7),
`alkalinity` (A, 0.20), `calcareous alkalinity` (0.9), `design life` (75 yr), `allowable loss` (1.0 in.),
`sulfide warning` (0.5 mg/L dissolved).

`[Reaches]`, in flow order: Reach, Type (`gravity`, or `force main` / `pressure` / `siphon` for full pipes), Length
(ft), Diameter (in.), Slope (%), n, Flow (gpm, average daily flow in the reach), Material.

## Method

- EBOD = BOD5·1.07^(T−20).
- Gravity reaches (Pomeroy–Parkhurst): d[S]/dt = M′·EBOD/r − m·(s·u)^⅜·[S]/dm (SI: r and dm = A/b in m, u m/s,
  s m/m, t h). Hydraulics are Manning normal depth at the average flow. With constant hydraulics the equation is
  linear, so each reach is solved exactly: [S]lim = (M′/m)·EBOD·(s·u)^−⅜·P/b and
  [S]out = [S]lim + ([S]in − [S]lim)·e^(−t/τ), τ = dm/(m·(s·u)^⅜). A gravity reach whose flow exceeds its capacity
  is treated as full (W-SC-004).
- Full pipes (force mains, siphons, surcharged reaches): Δ[S] = M·t·EBOD·(4/d + 1.57), d in m, t = L/V in h;
  M = 1.0×10⁻³ m/h over 6 in., 0.3×10⁻³ m/h at 6 in. and smaller. No oxygen, no loss.
- Where the flow rises, the added flow mixes in at `lateral sulfide`.
- H2S share of dissolved sulfide j = 1/(1 + 10^(pH − pK1)), pK1 = 6.98 + 0.017·(25 − T).
- Flux to the exposed wall φsw = 0.69·(s·u)^⅜·j·[DS]·b/P′ g/m²·h, P′ = πD − wetted perimeter, [DS] = dissolved
  fraction × the higher of [S]in and [S]out.
- Concrete corrosion c = 11.5·k·φsw/A mm/yr; loss over the design life = c·life; years to the allowable loss.
- Recommendation for each gravity reach: plain concrete if the loss over the life is within the allowable loss;
  otherwise calcareous aggregate (A ≈ 0.9) or the extra sacrificial thickness, if that would be enough; otherwise
  PVC, HDPE or PP pipe or RCP with a cast-in PVC/HDPE liner, with lined or coated manholes. PVC, HDPE, PP, VCP,
  fiberglass and anything described as lined count as resistant.
- Charts: total and limiting sulfide along the run; corrosion rate with the allowable rate.

## Decisions

- **D-750** Gravity reaches use Pomeroy–Parkhurst (1977) as EPA 1985 and MOP 69 present it, with M′ = 0.32×10⁻³ m/h
  and m = 0.64, the low-loss value that predicts more sulfide (0.96 is the other published value). Both can be changed.
  These values are from the published literature as recalled. They could not be checked against a copy of the EPA
  manual in this environment.
- **D-751** Full-flowing reaches use Pomeroy's full-pipe equation with the WSSC (2008) M values by size.
- **D-752** Build-up is computed at the average daily flow with a warm-season temperature, the condition the EPA
  manual designs for; peak flows shorten the travel time and are not the critical case.
- **D-753** Each reach is solved in closed form rather than by time steps, since its hydraulics are constant.
- **D-754** pK1 is a linear fit to tabulated values (7.24 at 10 °C, 7.06 at 20 °C, 6.98 at 25 °C); give `pK1` to
  override.
- **D-755** Corrosion is evaluated at the worse end of each reach with k = 0.7 and A = 0.20 (siliceous aggregate)
  by default. EPA gives k from 0.3 to 1.0 and A of 0.17 to 0.24 for siliceous and up to about 0.9 for calcareous
  aggregate.
- **D-756** The allowable loss defaults to 1 in., the usual concrete cover over the reinforcing (ASTM C76), over the
  IDCM §3.12.1 75-year life. Calcareous aggregate is offered only where A = 0.9 would bring the loss within the
  allowance.
- **D-757** Free tool, in the Sanitary sewers category with the lift station tool.
- **D-758** Not modelled: sulfide release by turbulence at drops, junctions and force main discharges, which are
  flagged in words only; oxygen in force mains; chemical dosing; and sulfide oxidation in the air space. Plastic and
  lined pipe are taken as immune, which leaves manholes as the exposed concrete.
- **D-759** U.S. units only, like the lift station tool. The equations are evaluated in SI internally because their
  coefficients are published in SI.
