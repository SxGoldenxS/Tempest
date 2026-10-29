"""Render Tempest's geometric app mark. Requires Pillow; not needed to build the app."""
from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1] / "src" / "Tempest" / "Assets"
root.mkdir(parents=True, exist_ok=True)
n = 1024
image = Image.new("RGBA", (n, n))
draw = ImageDraw.Draw(image)
# Original simple mark, with transparent padding and no tile or highlights.
def point(x, y):
    return ((x + 2) * n / 48, (y + 5) * n / 48)

def curve(a,b,c,d):
    return [((1-t)**3*a[0]+3*(1-t)**2*t*b[0]+3*(1-t)*t*t*c[0]+t**3*d[0],
             (1-t)**3*a[1]+3*(1-t)**2*t*b[1]+3*(1-t)*t*t*c[1]+t**3*d[1])
            for t in [i/100 for i in range(101)]]
outline=curve(point(2,29),point(0,14),point(10,3),point(22,3))
outline+=curve(point(22,3),point(35,3),point(45,15),point(42,29))
outline+=curve(point(42,29),point(38,36),point(7,36),point(2,29))
draw.polygon(outline,fill=(148,217,245,255))
draw.line([point(12,21),point(17,23)],fill=(36,67,89,255),width=round(2*n/48))
draw.line([point(27,23),point(32,21)],fill=(36,67,89,255),width=round(2*n/48))
image = image.resize((256,256), Image.Resampling.LANCZOS)
image.save(root / "tempest.ico", sizes=[(16,16),(20,20),(24,24),(32,32),(40,40),(48,48),(64,64),(128,128),(256,256)])
image.save(root / "tempest.png")
