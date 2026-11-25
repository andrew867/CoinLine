# Graphical advertising starter artwork

The sixteen operator-provided monochrome PNGs in `web/src/assets/graphical-starter` are 112 by 72 pixels. They form the enabled sample animation in the graphical starter. Each frame is displayed for 50 terminal ticks (500 ms). The final frame links to the first.

The packed resources are generated from these PNGs, not copied from a firmware image. Run `python scripts/pack-graphical-starter.py` at the repository root with Pillow installed to rebuild them. Each packed bitmap holds 1,008 pixel bytes; the native table codec supplies the surrounding record fields and padding. The sample menu is independently authored in the table codec, and contains local navigation only.

The starter does not include the disabled historical promotional artwork. Operators can use their own or appropriately licensed images in the graphical editor.
