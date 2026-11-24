"""Regenerate the original multiresolution Portwatch Desktop icon (requires Pillow)."""
from PIL import Image, ImageDraw
from pathlib import Path

size = 512
art = Image.new("RGBA", (size, size))
mask = Image.new("L", (size, size))
ImageDraw.Draw(mask).rounded_rectangle((12, 12, size-13, size-13), radius=112, fill=255)
gradient = Image.new("RGBA", (size, size))
d = ImageDraw.Draw(gradient)
for y in range(size):
    t = y / (size-1)
    color = tuple(round(a+(b-a)*t) for a,b in zip((255, 208, 141), (202, 89, 209))) + (255,)
    d.line((0,y,size,y), fill=color)
art.paste(gradient, (0,0), mask)
d = ImageDraw.Draw(art)
d.ellipse((103,72,409,378), fill="#FFE0A1")
for y,w in ((274,150),(305,135),(336,112)):
    d.line((256-w,y,256+w,y),fill="#DA77AA",width=15)
palm="#2A1935"
d.line([(156,445),(179,383),(203,314),(221,245),(226,156)],fill=palm,width=28,joint="curve")
for points in [[(226,156),(186,108),(138,111),(95,164)],[(226,156),(199,71),(151,64),(114,100)],[(226,156),(252,72),(311,69),(352,109)],[(226,156),(307,111),(363,122),(409,175)],[(226,156),(294,167),(324,222),(321,258)]]:
    d.line(points,fill=palm,width=23,joint="curve")
out=Path(__file__).resolve().parent.parent/"portwatch.ico"
art.save(out, sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
