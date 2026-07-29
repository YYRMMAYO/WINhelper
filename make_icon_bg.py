from PIL import Image

W, H = 164, 314
# 品牌蓝渐变背景 (#4A90D9 -> 更深的蓝)
top = (0x4A, 0x90, 0xD9)
bot = (0x2E, 0x5F, 0xA8)
img = Image.new("RGB", (W, H))
px = img.load()
for y in range(H):
    t = y / (H - 1)
    r = int(top[0] + (bot[0] - top[0]) * t)
    g = int(top[1] + (bot[1] - top[1]) * t)
    b = int(top[2] + (bot[2] - top[2]) * t)
    for x in range(W):
        px[x, y] = (r, g, b)

# 软件图标（带透明通道），居中放置；若 ico 失败则回退到 jpg
try:
    icon = Image.open("AppIcon.ico").convert("RGBA")
except Exception:
    icon = Image.open("软件图标.jpg").convert("RGBA")

max_h = 120
ratio = max_h / icon.height
new_w, new_h = int(icon.width * ratio), max_h
icon = icon.resize((new_w, new_h), Image.LANCZOS)
x = (W - new_w) // 2
y = (H - new_h) // 2 - 10
img.paste(icon, (x, y), icon)

img.save("setup_bg.bmp", "BMP")
print("setup_bg.bmp generated:", img.size)
