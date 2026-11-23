# Manual screenshots

Renders the real MasterFlow windows headless (Avalonia.Headless with Skia) and saves the PNGs used in
`docs/USER_GUIDE.md`. Not part of the solution and not shipped.

```
dotnet run -c Release --project tools/ManualScreenshots -- <out folder> docs/samples/storm-hgl-demo.mfproj [main|net|tools|<tool id>]
```

It opens the built-in sample calculations plus the *Main St storm* network, captures the main window tabs and the
Storm networks tabs, then selects every design tool, loads its **Sample**, runs it and captures the Design tools
window. Each tool's HTML report is written to `<out>/reports`, and `<out>/tools.tsv` lists whether each sample ran
without errors. On Linux the Consolas text panes are drawn with DejaVu Sans Mono (Consolas is a Windows font).

The manual's chart figures are the first SVG of each HTML report, captured with Playwright, and all images are
reduced to 256-colour PNG (`convert in.png -strip -colors 256 PNG8:out.png`).
