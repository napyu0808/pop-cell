"""
PopCellKR.ttf 재생성 — Localization.cs 전체 텍스트 기준 서브셋.

  round45: 게임 컨셉에 맞춘 둥근 글꼴로 교체 (사용자 승인, 2026-09-29).
    1) Jua (한글·라틴, 둥근 손글씨풍)            — 기본
    2) Kosugi Maru (일본어 가나·한자, 둥근 고딕)  — Jua 에 없는 글자
    3) Noto Sans KR / 4) Noto Sans JP            — 그래도 없는 기호(×, ·, ▲▼▶, ∞ …)
  앞 글꼴에 없는 글자만 뒤 글꼴에서 골라 서브셋한 뒤 한 파일로 합친다.
  합치려면 unitsPerEm 이 같아야 해서(Kosugi Maru 1024) 전부 1000 으로 맞춘다.
  IMGUI 는 글자 합성(GSUB/GPOS)을 안 쓰므로 합칠 때 충돌하는 레이아웃·세로쓰기 표는 뺀다.

  ※ 에디터가 켜진 채로 폰트를 바꾸면 Unity 6 IMGUI 가 옛 글자 정보를 캐시해서 엉뚱한 글자가
    보인다 — 폰트를 다시 만든 뒤엔 유니티를 한 번 재시작할 것(빌드는 영향 없음).
"""
import pathlib, subprocess, sys, shutil
from fontTools.ttLib import TTFont
from fontTools.ttLib.scaleUpem import scale_upem

ROOT = pathlib.Path(__file__).resolve().parent.parent
LOC = ROOT / "My project" / "Assets" / "Scripts" / "BlackholeGame" / "Localization.cs"
OUT = ROOT / "My project" / "Assets" / "Resources" / "PopCellKR.ttf"
WORK = pathlib.Path(__file__).resolve().parent
FONTS = pathlib.Path(r"C:\Users\ADMINI~1\AppData\Local\Temp\claude\C--Users-Administrator-Desktop-AutoClickerGame\a9375838-d49f-4434-8fb5-09e8dacd67a7\scratchpad")
CHAIN = [FONTS / "Jua-Regular.ttf", FONTS / "KosugiMaru-Regular.ttf",
         FONTS / "NotoSansKR-Regular.ttf", FONTS / "NotoSansJP-Regular.ttf"]
DROP = "GSUB,GPOS,GDEF,BASE,vhea,vmtx,VORG,DSIG,morx,kerx"

# Localization.cs 밖(GameManager 등)에서 코드로 직접 그리는 UI 기호들.
EXTRA = "▲▼▶"   # ▶(U+25B6) 는 있음. ▸(U+25B8) 는 Noto KR/JP 둘 다 없음 — 쓰지 말 것


def run(cmd):
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        print("FAILED:", cmd, "\n", r.stdout, r.stderr)
        sys.exit(1)


text = LOC.read_text(encoding="utf-8") + EXTRA
need = sorted(set(ch for ch in text if ord(ch) > 0x20))
remaining = list(need)
parts = []
for i, src in enumerate(CHAIN):
    if not remaining:
        break
    cmap = TTFont(str(src), lazy=True).getBestCmap()
    take = [c for c in remaining if ord(c) in cmap]
    if not take:
        continue
    txt = WORK / ("_sub_%d.txt" % i)
    txt.write_text("".join(take), encoding="utf-8")
    out = WORK / ("_sub_%d.ttf" % i)
    run(["python", "-m", "fontTools.subset", str(src), f"--output-file={out}", f"--text-file={txt}",
         "--glyph-names", f"--drop-tables+={DROP}", "--notdef-outline"])
    f = TTFont(str(out))
    if f["head"].unitsPerEm != 1000:
        scale_upem(f, 1000)
        f.save(str(out))
    parts.append(out)
    remaining = [c for c in remaining if ord(c) not in cmap]
    print("%-24s %4d 글자" % (src.name, len(take)))

if remaining:
    print("!!! 어느 글꼴에도 없는 글자:", "".join(remaining))
    sys.exit(1)

merged = WORK / "_merged.ttf"
if len(parts) == 1:
    shutil.copy(str(parts[0]), str(merged))
else:
    run(["python", "-m", "fontTools.merge", *[str(p) for p in parts], f"--output-file={merged}"])

ff = TTFont(str(merged))
cm = ff.getBestCmap()
still = [c for c in need if ord(c) not in cm and c not in "\t\n\r"]
print("최종 결손:", len(still), "".join(still))
if still:
    sys.exit(1)
shutil.copy(str(merged), str(OUT))
print("완료 ->", OUT, OUT.stat().st_size, "bytes, glyphs:", ff["maxp"].numGlyphs, "family:", ff["name"].getDebugName(1))
for p in parts + [merged] + list(WORK.glob("_sub_*.txt")):
    try:
        p.unlink()
    except Exception:
        pass
