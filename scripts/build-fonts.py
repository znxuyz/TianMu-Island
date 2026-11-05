#!/usr/bin/env python3
"""
天幕島 · 宋體煉製腳本

收集程式中所有介面文字（XAML 與 C# 字串）用到的字，
向 Google Fonts 只索取這些字的「思源宋體 Noto Serif TC」，存成小巧的 TTF 內嵌進程式。

用法（在專案根目錄）：
    python scripts/build-fonts.py          # 重新產生字型
    python scripts/build-fonts.py --check  # 只檢查是否有字缺漏（CI 使用，需要 fonttools）

新增介面文字後若宋體缺字，重新執行一次即可。
字型授權：SIL Open Font License 1.1（見 src/TianMu.Island/Assets/Fonts/OFL.txt）。
"""
import pathlib
import re
import sys
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
OUT = SRC / "TianMu.Island" / "Assets" / "Fonts"
WEIGHTS = {500: "NotoSerifTC-Medium.ttf", 700: "NotoSerifTC-Bold.ttf"}

# 一定要有的字：數字、時間符號、常用標點
BASE = "0123456789:：·・、，。！？「」『』（）()…—-+/ v.%年月日時"

# 這些檔案裡的字串是比對規則（正規表示式、簡體關鍵字），不會顯示在畫面上
NOT_UI = {"TrackTitleCleaner.cs"}

CS_STRING = re.compile(r'"(?:[^"\\\n]|\\.)*"')
XML_COMMENT = re.compile(r"<!--.*?-->", re.S)


def collect() -> str:
    chars = set(BASE)
    for path in SRC.rglob("*"):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        if path.suffix == ".xaml":
            text = XML_COMMENT.sub("", path.read_text(encoding="utf-8"))
            chars.update(c for c in text if ord(c) >= 0x2E80)
        elif path.suffix == ".cs" and path.name not in NOT_UI:
            for literal in CS_STRING.findall(path.read_text(encoding="utf-8")):
                chars.update(c for c in literal if ord(c) >= 0x2E80)
    # 只保留宋體可能有的字（排除私用區的圖示字元）
    return "".join(sorted(c for c in chars if not (0xE000 <= ord(c) <= 0xF8FF)))


def fetch(weight: int, text: str) -> bytes:
    query = urllib.parse.urlencode({"family": f"Noto Serif TC:wght@{weight}", "text": text})
    css_url = f"https://fonts.googleapis.com/css2?{query}"
    # 舊版瀏覽器的 User-Agent 會拿到 TrueType（WPF 不支援 WOFF2）
    req = urllib.request.Request(css_url, headers={"User-Agent": "Mozilla/4.0"})
    css = urllib.request.urlopen(req, timeout=60).read().decode("utf-8")
    match = re.search(r"url\((https://[^)]+)\)\s*format\('truetype'\)", css)
    if not match:
        raise RuntimeError(f"Google Fonts 沒有回傳 TrueType 字型：\n{css[:500]}")
    return urllib.request.urlopen(match.group(1), timeout=120).read()


def check(text: str) -> int:
    from fontTools.ttLib import TTFont

    missing_total = 0
    for name in WEIGHTS.values():
        path = OUT / name
        if not path.exists():
            print(f"✗ 缺少字型檔 {name}，請執行 python scripts/build-fonts.py")
            missing_total += 1
            continue
        cmap = TTFont(path).getBestCmap()
        missing = [c for c in text if ord(c) >= 0x2E80 and ord(c) not in cmap]
        if missing:
            print(f"✗ {name} 缺 {len(missing)} 字：{''.join(missing)}")
            missing_total += len(missing)
        else:
            print(f"✓ {name} 涵蓋全部 {len(text)} 字")
    return missing_total


def main() -> int:
    text = collect()
    if "--check" in sys.argv:
        missing = check(text)
        if missing:
            print("請執行 python scripts/build-fonts.py 重新煉製宋體。")
        return 1 if missing else 0

    OUT.mkdir(parents=True, exist_ok=True)
    print(f"收集到 {len(text)} 個字")
    for weight, name in WEIGHTS.items():
        data = fetch(weight, text)
        (OUT / name).write_bytes(data)
        print(f"✓ {name}  {len(data) / 1024:.0f} KB")
    return 0


if __name__ == "__main__":
    sys.exit(main())
