"""從 CHANGELOG.md 取出某一版的更新內容，寫成 GitHub Release 的說明。

用法：
  python scripts/release-notes.py v0.19.0 notes.md       # 單一版本寫成檔案（發佈流程用）
  python scripts/release-notes.py v0.19.0                # 單一版本印到畫面
  python scripts/release-notes.py --all <輸出資料夾>         # 每個版本各一個檔案（補寫舊版本用）
"""
import re
import sys
from pathlib import Path

REPO = "znxuyz/TianMu-Island"
CHANGELOG = Path(__file__).resolve().parent.parent / "CHANGELOG.md"
HEADING = re.compile(r"^## \[(v[^\]]+)\]")

DOWNLOAD = ("**下載**：一般電腦選 `TianMuIsland-win-x64.exe`，ARM 電腦（例如 Snapdragon 筆電）選 `TianMuIsland-win-arm64.exe`；"
            "點兩下就能執行，已安裝的天幕島會自動提示更新。")


def sections() -> list[tuple[str, list[str]]]:
    """[(版本, 內容行)]，新到舊。"""
    out: list[tuple[str, list[str]]] = []
    for line in CHANGELOG.read_text(encoding="utf-8").splitlines():
        m = HEADING.match(line)
        if m:
            out.append((m.group(1), []))
        elif out:
            out[-1][1].append(line)
    return out


def notes(tag: str) -> str:
    all_ = sections()
    tags = [t for t, _ in all_]
    if tag not in tags:
        return f"這一版的更新內容請見 [CHANGELOG.md](https://github.com/{REPO}/blob/main/CHANGELOG.md)。\n\n{DOWNLOAD}\n"
    i = tags.index(tag)
    body = "\n".join(all_[i][1]).strip()
    parts = [body, "", DOWNLOAD]
    if i + 1 < len(tags):
        parts += ["", f"**完整變更**：https://github.com/{REPO}/compare/{tags[i + 1]}...{tag}"]
    return "\n".join(parts) + "\n"


def main() -> int:
    if len(sys.argv) == 3 and sys.argv[1] == "--all":
        out = Path(sys.argv[2])
        out.mkdir(parents=True, exist_ok=True)
        for tag, _ in sections():
            (out / f"{tag}.md").write_text(notes(tag), encoding="utf-8")
            print(tag)
        return 0
    if len(sys.argv) == 3:
        Path(sys.argv[2]).write_text(notes(sys.argv[1]), encoding="utf-8")   # 直接寫 UTF-8，不經過主控台編碼
        return 0
    if len(sys.argv) == 2:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stdout.write(notes(sys.argv[1]))
        return 0
    print(__doc__, file=sys.stderr)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
