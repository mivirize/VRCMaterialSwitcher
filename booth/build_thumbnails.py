"""BOOTH 商品ページ用の画像を組む。

  python booth/build_thumbnails.py

出力: dist/thumbnails/*.png （1200x630 / BOOTH 推奨比率）
"""
import os
from PIL import Image, ImageDraw, ImageFont

REPO = r'C:/Users/rmiak/VRCMaterialSwitcher'
SHOTS = (r'C:/Users/rmiak/AppData/Local/Temp/claude/'
         r'c--Users-rmiak-Dev/977c4d89-58ef-4266-a0f3-b7ad60ed1cdb/scratchpad/shots')
OUT = os.path.join(REPO, 'dist', 'thumbnails')
os.makedirs(OUT, exist_ok=True)

W, H = 1200, 630
BG = (24, 26, 33)
PANEL = (32, 35, 44)
TEXT = (238, 240, 245)
MUTED = (150, 158, 175)
ACCENT = (108, 152, 255)

FONTS = [
    r'C:/Windows/Fonts/YuGothB.ttc',
    r'C:/Windows/Fonts/meiryob.ttc',
    r'C:/Windows/Fonts/msgothic.ttc',
]
FONTS_R = [
    r'C:/Windows/Fonts/YuGothR.ttc',
    r'C:/Windows/Fonts/meiryo.ttc',
    r'C:/Windows/Fonts/msgothic.ttc',
]


def font(size, bold=True):
    for path in (FONTS if bold else FONTS_R):
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue
    return ImageFont.load_default()


def fit(img, box_w, box_h):
    """box に収まるよう縮小（アスペクト維持）。"""
    r = min(box_w / img.width, box_h / img.height)
    if r < 1:
        img = img.resize((int(img.width * r), int(img.height * r)), Image.LANCZOS)
    return img


def shadowed(canvas, img, x, y):
    sh = Image.new('RGB', (img.width + 8, img.height + 8), (12, 13, 17))
    canvas.paste(sh, (x - 4, y - 4))
    canvas.paste(img, (x, y))


def main_thumb():
    c = Image.new('RGB', (W, H), BG)
    d = ImageDraw.Draw(c)

    # 左: タイトル
    d.text((64, 92), 'VRC Material', font=font(64), fill=TEXT)
    d.text((64, 168), 'Switcher', font=font(64), fill=ACCENT)
    d.text((64, 268), '衣装の色替えメニューを', font=font(30, False), fill=TEXT)
    d.text((64, 312), '自動でつくる', font=font(30, False), fill=TEXT)

    for i, line in enumerate([
        '・フォルダをスキャンするだけ',
        '・アニメーションの知識は不要',
        '・アバターを改変しない（非破壊）',
    ]):
        d.text((66, 388 + i * 40), line, font=font(23, False), fill=MUTED)

    d.text((64, H - 78), 'Unity 2022.3 / VRChat SDK3 / Modular Avatar',
           font=font(20, False), fill=MUTED)
    d.text((64, H - 46), 'MIVI Works', font=font(22), fill=ACCENT)

    # 右: 実画面
    shot = fit(Image.open(f'{SHOTS}/shot-scanned.png'), 470, 560)
    shadowed(c, shot, W - shot.width - 56, (H - shot.height) // 2)

    p = f'{OUT}/01_main.png'
    c.save(p)
    return p


def concept_thumb():
    c = Image.new('RGB', (W, H), BG)
    d = ImageDraw.Draw(c)
    d.text((64, 54), 'フォルダ → ゲーム内メニュー', font=font(42), fill=TEXT)
    d.text((64, 116), 'マテリアルの置き方から色の組み合わせを推測します',
           font=font(24, False), fill=MUTED)

    # 左パネル: フォルダ
    d.rounded_rectangle([64, 186, 520, 520], 14, fill=PANEL)
    d.text((96, 214), '衣装のマテリアル', font=font(24), fill=ACCENT)
    for i, line in enumerate([
        'Onepiece_Black.mat',
        'Onepiece_White.mat',
        'Onepiece_Red.mat',
        'Ribbon_Blue.mat',
        'Ribbon_Pink.mat',
    ]):
        d.text((96, 268 + i * 42), line, font=font(23, False), fill=TEXT)

    # 矢印
    d.polygon([(556, 340), (556, 366), (614, 366), (614, 386),
               (652, 353), (614, 320), (614, 340)], fill=ACCENT)

    # 右パネル: メニュー
    d.rounded_rectangle([684, 186, 1136, 520], 14, fill=PANEL)
    d.text((716, 214), 'Expression Menu', font=font(24), fill=ACCENT)
    d.text((716, 268), '衣装カラー', font=font(25), fill=TEXT)
    for i, (label, colors) in enumerate([
        ('Onepiece', 'Black / White / Red'),
        ('Ribbon', 'Blue / Pink'),
    ]):
        y = 320 + i * 78
        # フォントに無い記号（▸ 等）は豆腐になるため図形で描く
        d.polygon([(746, y + 6), (746, y + 22), (759, y + 14)], fill=ACCENT)
        d.text((772, y), label, font=font(23), fill=TEXT)
        d.text((772, y + 34), colors, font=font(21, False), fill=MUTED)

    p = f'{OUT}/02_concept.png'
    c.save(p)
    return p


def panel_thumb(shot_name, title, caption, out_name, box=(760, 520)):
    c = Image.new('RGB', (W, H), BG)
    d = ImageDraw.Draw(c)
    d.text((64, 48), title, font=font(38), fill=TEXT)
    d.text((64, 104), caption, font=font(23, False), fill=MUTED)
    shot = fit(Image.open(f'{SHOTS}/{shot_name}'), *box)
    shadowed(c, shot, (W - shot.width) // 2, 150)
    p = f'{OUT}/{out_name}'
    c.save(p)
    return p


if __name__ == '__main__':
    made = [
        main_thumb(),
        concept_thumb(),
        panel_thumb('shot-scanned.png', 'スキャン結果を確認して調整',
                    '検出された色を確認し、不要な色を外したり初期色を選べます',
                    '03_scan.png', (430, 470)),
        panel_thumb('shot-setup.png', 'セットアップ実行',
                    'パラメータ消費量とテクスチャ容量の試算も表示されます',
                    '04_setup.png', (450, 470)),
        panel_thumb('shot-inspector.png', '生成されるのは Modular Avatar だけ',
                    'アバター本体は改変しません。この階層を消せば元通りです',
                    '05_nondestructive.png', (300, 470)),
    ]
    for m in made:
        im = Image.open(m)
        print(f'  {os.path.basename(m):28s} {im.width}x{im.height}  '
              f'{os.path.getsize(m)/1024:6.1f} KB')
