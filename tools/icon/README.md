# Plugin icon generator

`make_icon.py` draws the plugin icon: a 2x2 virtual desktop pager whose active desktop is lit and
holds a window, matte, night blue. It follows the Audio plugin icon (same tile, colours, matte parts
and red marker), so the plugin icons read as a family. The icon is original artwork, no third-party
source; the KDE and Plasma logos are deliberately not used or imitated.

```bash
pip install pillow numpy
python tools/icon/make_icon.py tools/icon/out
cp tools/icon/out/icon_256.png icon.png
```

The script writes `icon_{256,128,64,32,16}.png` into the given folder; only the 256 px file is
used, as `icon.png` in the repo root. The `out/` folder is not committed.
