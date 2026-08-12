"""BOOTH 配布用 ZIP を組み立てる。

  python booth/build_booth_package.py

出力: dist/MIVIWorks_VRCMaterialSwitcher_v<VER>.zip
      （中身は vrc-booth-pipeline §2 の DL ファイル構成規約に準拠）
"""
import os
import re
import shutil
import zipfile

VER = '1.2.3'
REPO = r'C:/Users/rmiak/VRCMaterialSwitcher'
SCRATCH = (r'C:/Users/rmiak/AppData/Local/Temp/claude/'
           r'c--Users-rmiak-Dev/977c4d89-58ef-4266-a0f3-b7ad60ed1cdb/scratchpad')

NAME = f'MIVIWorks_VRCMaterialSwitcher_v{VER}'
STAGE = os.path.join(REPO, 'dist', NAME)
DIST = os.path.join(REPO, 'dist')

BLOCKLIST = [
    'komano', '_vmstest', '_vmsmanual', '_vmsdiag', '_vmsui',
    'vmsverif', 'vmsuiprobe', 'vmswidth', 'vmsmanualshots',
    'gesturemanager', 'packageexporter',
    'vrcsdk', 'clientsim', 'miviworks/ss',
]


def fail(msg):
    raise SystemExit(f'BUILD FAILED: {msg}')


def stage():
    if os.path.exists(STAGE):
        shutil.rmtree(STAGE)
    os.makedirs(STAGE)

    # 1) unitypackage（GitHub Release と同一のものを製品名にリネームして格納）
    src_pkg = os.path.join(REPO, f'VRCMaterialSwitcher-v{VER}.unitypackage')
    if not os.path.exists(src_pkg):
        fail(f'unitypackage が見つかりません: {src_pkg}')
    shutil.copy2(src_pkg, os.path.join(STAGE, f'{NAME}.unitypackage'))

    # 2) README / LICENSE（booth/ の正本をコピー）
    for f in ('README.txt', 'LICENSE.txt'):
        shutil.copy2(os.path.join(REPO, 'booth', f), os.path.join(STAGE, f))

    # 3) CHANGELOG（Markdown 記法を落としてテキスト化）
    md = open(os.path.join(REPO, 'CHANGELOG.md'), encoding='utf-8').read()
    txt = re.sub(r'^#+\s*', '', md, flags=re.M)
    txt = txt.replace('**', '').replace('`', '')
    header = ('========================================================\n'
              ' VRC Material Switcher  変更履歴\n'
              '========================================================\n\n')
    with open(os.path.join(STAGE, 'CHANGELOG.txt'), 'w',
              encoding='utf-8', newline='\r\n') as f:
        f.write(header + txt)

    # 4) マニュアル（オフラインで開ける完全版。スクショ埋め込み済み）
    manual_src = os.path.join(SCRATCH, 'vms-manual-final.html')
    if not os.path.exists(manual_src):
        fail('マニュアル HTML が見つかりません')
    body = open(manual_src, encoding='utf-8').read()
    html = ('<!doctype html>\n<html lang="ja">\n<head>\n'
            '<meta charset="utf-8">\n'
            '<meta name="viewport" content="width=device-width,initial-scale=1">\n'
            + body.split('</style>')[0] + '</style>\n</head>\n<body>\n'
            + '</style>'.join(body.split('</style>')[1:]) + '\n</body>\n</html>\n')
    with open(os.path.join(STAGE, 'MANUAL.html'), 'w', encoding='utf-8') as f:
        f.write(html)

    # README を Windows 改行に揃える（メモ帳で開いても崩れないように）
    for f in ('README.txt', 'LICENSE.txt'):
        p = os.path.join(STAGE, f)
        data = open(p, encoding='utf-8').read().replace('\r\n', '\n')
        open(p, 'w', encoding='utf-8', newline='\r\n').write(data)


def leak_check():
    """unitypackage の中身をブロックリストで検査する。"""
    import tarfile
    pkg = os.path.join(STAGE, f'{NAME}.unitypackage')
    names = []
    with tarfile.open(pkg, 'r:gz') as tf:
        for m in tf.getmembers():
            if m.name.endswith('pathname'):
                names.append(tf.extractfile(m).read().decode('utf-8').strip())
    if not names:
        fail('unitypackage から pathname を読めません')

    hits = [n for n in names for b in BLOCKLIST if b in n.lower()]
    if hits:
        fail(f'梱包リーク: {hits}')
    print(f'  リーク検査 OK: {len(names)} ファイル')
    for n in sorted(names):
        print(f'    {n}')
    return names


def zip_it():
    zip_path = os.path.join(DIST, f'{NAME}.zip')
    if os.path.exists(zip_path):
        os.remove(zip_path)
    with zipfile.ZipFile(zip_path, 'w', zipfile.ZIP_DEFLATED) as z:
        for root, _, files in os.walk(STAGE):
            for fn in files:
                full = os.path.join(root, fn)
                rel = os.path.relpath(full, os.path.dirname(STAGE))
                z.write(full, rel)
    return zip_path


if __name__ == '__main__':
    stage()
    names = leak_check()
    zp = zip_it()
    print('\n=== 同梱ファイル ===')
    for f in sorted(os.listdir(STAGE)):
        size = os.path.getsize(os.path.join(STAGE, f))
        print(f'  {f:52s} {size/1024:8.1f} KB')
    print(f'\nZIP: {zp}  ({os.path.getsize(zp)/1024:.1f} KB)')
