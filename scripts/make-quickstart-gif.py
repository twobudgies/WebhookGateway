"""Re-creates docs/media/quickstart.gif: a terminal animation condensed from a real run of docker compose and scripts/quickstart.sh (keys shortened).

Usage: python3 -m venv /tmp/gif && /tmp/gif/bin/pip install pillow && /tmp/gif/bin/python scripts/make-quickstart-gif.py
The text is in the `script` list below; update it when the quick start output changes. Needs the Menlo font (macOS); change the font path on Linux.
"""
import os
OUTPUT = os.path.join(os.path.dirname(__file__), "..", "docs", "media", "quickstart.gif")
from PIL import Image, ImageDraw, ImageFont
W, H, PAD, LINE = 900, 470, 22, 21
BG, FG, DIM, GREEN, CYAN, YEL = (13,17,23), (230,237,243), (125,133,144), (63,185,80), (88,166,255), (210,153,34)
font = ImageFont.truetype("/System/Library/Fonts/Menlo.ttc", 14)
bold = ImageFont.truetype("/System/Library/Fonts/Menlo.ttc", 14, index=1)
title = ImageFont.truetype("/System/Library/Fonts/Menlo.ttc", 12)

# (kind, text): "cmd" is typed after a prompt, "out" appears at once, "pause" waits, "gap" is an empty line
script = [
 ("cmd", "git clone https://github.com/twobudgies/WebhookGateway && cd WebhookGateway"),
 ("pause", 0.9),
 ("cmd", "docker compose up -d"),
 ("out", " Container webhookgateway-postgres-1    Healthy", DIM),
 ("out", " Container webhookgateway-init-1        Exited", DIM),
 ("out", " Container webhookgateway-migrator-1    Exited", DIM),
 ("out", " Container webhookgateway-ingest-1      Started", DIM),
 ("out", " Container webhookgateway-fanout-1      Started", DIM),
 ("out", " Container webhookgateway-dispatcher-1  Started", DIM),
 ("out", " Container webhookgateway-controlplane-1 Started", DIM),
 ("pause", 1.6),
 ("cmd", "./scripts/quickstart.sh"),
 ("out", "1/6 Waiting for the services", CYAN),
 ("out", "2/6 Reading the admin key generated on first start", CYAN),
 ("out", "3/6 Creating a customer (tenant)", CYAN),
 ("out", "    tenant id: 01a16006-112e-7d91-9a15-dae8c3be0321", FG),
 ("out", "4/6 Creating an API key for the customer (shown only once)", CYAN),
 ("out", "    api key:   wgk_live_bePm...", FG),
 ("out", "5/6 Registering the demo receiver as the customer's endpoint", CYAN),
 ("out", "    signing secret: whsec_w4zA...", FG),
 ("out", "6/6 Sending an event", CYAN),
 ("out", '    {"eventId":"evt_01a16006-11be-74d7-b0b2-502b926afe26"}', FG),
 ("pause", 1.6),
 ("out", "    Waiting for delivery...", DIM),
 ("pause", 1.5),
 ("out", '    [{"signature":"valid","body":{"type":"order.created","data":{"orderId":42,"amount":1999}}}]', GREEN),
 ("pause", 1.0),
 ("out", "", FG),
 ("out", "Done. Ingest -> queue -> Fan-out -> queue -> Dispatcher -> signed POST -> receiver.", GREEN),
 ("out", "Open http://localhost:9000 to see it. Docs: README.md, Swagger at /docs.", DIM),
 ("hold", 5.0),
]
frames, durations = [], []
lines = []  # (text, color)
cur = None   # currently typed command text
def render(prompt_line=None):
    img = Image.new("RGB", (W, H), BG); d = ImageDraw.Draw(img)
    d.rectangle([0, 0, W, 34], fill=(22, 27, 34))
    for i, c in enumerate([(255,95,86),(255,189,46),(39,201,63)]): d.ellipse([16+i*22, 12, 28+i*22, 24], fill=c)
    d.text((W//2-95, 11), "WebhookGateway - quick start", fill=DIM, font=title)
    visible = lines + ([prompt_line] if prompt_line else [])
    max_lines = (H - 34 - PAD) // LINE
    for i, item in enumerate(visible[-max_lines:]):
        y = 34 + PAD//2 + i*LINE
        if item[0] == "prompt":
            d.text((PAD, y), "$ ", fill=GREEN, font=bold)
            d.text((PAD+22, y), item[1] + ("_" if item[2] else ""), fill=FG, font=font)
        else:
            d.text((PAD, y), item[1], fill=item[2], font=font)
    return img
def add(img, dur):
    if frames and img.tobytes() == frames[-1].tobytes() :
        durations[-1] += dur; return
    frames.append(img); durations.append(dur)
add(render(("prompt", "", True)), 0.6)
for step in script:
    kind = step[0]
    if kind == "cmd":
        text = step[1]
        for i in range(0, len(text)+1, 2):
            add(render(("prompt", text[:i], True)), 0.06)
        add(render(("prompt", text, True)), 0.6)
        lines.append(("prompt", text, False))
    elif kind == "out":
        lines.append(("out", step[1], step[2])); add(render(), 0.32)
    elif kind in ("pause", "hold"):
        add(render(), step[1])
q = frames[-1].convert("P", palette=Image.ADAPTIVE, colors=24)
pal = [f.quantize(palette=q, dither=Image.Dither.NONE) for f in frames]
pal[0].save(OUTPUT, save_all=True, append_images=pal[1:], duration=[int(x*1000) for x in durations], loop=0, optimize=True, disposal=1)
print(len(frames), "frames", round(sum(durations),1), "seconds")

