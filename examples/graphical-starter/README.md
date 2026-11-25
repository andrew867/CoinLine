# Graphical advertising starter artwork

The twenty operator-provided monochrome PNGs in `web/src/assets/graphical-starter` are 112 by 72 pixels. The first sixteen form the globe animation, each displayed for 50 terminal ticks (500 ms). Screens 17 through 20 are the original static brand and promotional graphics, each displayed for 500 ticks (5 seconds). Screen 20 links back to screen 1.

The packed resources are generated from these PNGs, not copied from a firmware image. Run `python scripts/pack-graphical-starter.py` at the repository root with Pillow installed to rebuild them. Each packed bitmap holds 1,008 pixel bytes; the native table codec supplies the surrounding record fields and padding. The sample menu is independently authored in the table codec, and contains local navigation only.

The supplied sample artwork is included in the enabled on-hook rotation. Operators can replace the images or adjust their durations in the graphical editor. The sample menu starts at screen 21 and is not part of the on-hook rotation.
