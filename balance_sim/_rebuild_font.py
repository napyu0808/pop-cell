"""
PopCellKR.ttf 재생성 — 게임이 실제로 그리는 글자만 담은 서브셋.

  round45: 게임 컨셉에 맞춘 둥근 글꼴로 교체 (사용자 승인, 2026-09-29).
    1) Jua (한글·라틴, 둥근 손글씨풍)            — 기본
    2) Kosugi Maru (일본어 가나·한자, 둥근 고딕)  — Jua 에 없는 글자
    3) Noto Sans KR / 4) Noto Sans JP            — 그래도 없는 기호(×, ·, ▲▼▶, ∞ …)
  앞 글꼴에 없는 글자만 뒤 글꼴에서 골라 서브셋한 뒤 한 파일로 합친다.
  합치려면 unitsPerEm 이 같아야 해서 전부 1000 으로 맞춘다.
  IMGUI 는 글자 합성(GSUB/GPOS)을 안 쓰므로 합칠 때 충돌하는 레이아웃·세로쓰기 표는 뺀다.

  ※ 에디터가 켜진 채로 폰트를 바꾸면 Unity 6 IMGUI 가 옛 글자 정보를 캐시해서 엉뚱한 글자가
    보인다 — 폰트를 다시 만든 뒤엔 유니티를 한 번 재시작할 것(빌드는 영향 없음).

  ---- 2026-10-26 에 고친 것 ----
  * 원본 글꼴이 옛 세션의 임시 폴더에 있어서 그 폴더가 사라지자 재생성이 불가능해졌다.
    이제 저장소 안 `_fonts/` 에 두고, 없으면 Google Fonts 에서 받아온다(.gitignore 대상).
  * Noto 두 개는 가변 폰트라 그대로 합치면 fvar 가 남는다 — wght 400 으로 고정한 사본을 쓴다.
  * 서브셋 기준이 Localization.cs 하나뿐이라, 코드가 직접 그리는 글자는 폰트에 없어서
    화면에서 그냥 사라졌다("닫기" 가 "기" 로 보이던 문제). 이제 모든 .cs 의 문자열 리터럴을 같이 본다.
"""
import pathlib, re, subprocess, sys, shutil, urllib.request
from fontTools.ttLib import TTFont
from fontTools.ttLib.scaleUpem import scale_upem
from fontTools.varLib.instancer import instantiateVariableFont

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "My project" / "Assets" / "Scripts" / "BlackholeGame"
LOC = SRC / "Localization.cs"
OUT = ROOT / "My project" / "Assets" / "Resources" / "PopCellKR.ttf"
WORK = pathlib.Path(__file__).resolve().parent
FONTS = WORK / "_fonts"
GF = "https://github.com/google/fonts/raw/main"
SOURCES = [
    ("Jua-Regular.ttf",        GF + "/ofl/jua/Jua-Regular.ttf"),
    ("KosugiMaru-Regular.ttf", GF + "/apache/kosugimaru/KosugiMaru-Regular.ttf"),
    ("NotoSansKR[wght].ttf",   GF + "/ofl/notosanskr/NotoSansKR%5Bwght%5D.ttf"),
    ("NotoSansJP[wght].ttf",   GF + "/ofl/notosansjp/NotoSansJP%5Bwght%5D.ttf"),
]
DROP = "GSUB,GPOS,GDEF,BASE,vhea,vmtx,VORG,DSIG,morx,kerx"

# Localization.cs 밖(GameManager 등)에서 코드로 직접 그리는 UI 기호들.
EXTRA = "▲▼▶"   # ▶(U+25B6) 는 있음. ▸(U+25B8) 는 Noto KR/JP 둘 다 없음 — 쓰지 말 것


def run(cmd):
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        print("FAILED:", cmd, "\n", r.stdout, r.stderr)
        sys.exit(1)


def ensure_sources():
    FONTS.mkdir(exist_ok=True)
    for name, url in SOURCES:
        p = FONTS / name
        if p.exists() and p.stat().st_size > 50_000:
            continue
        print("받는 중:", name)
        urllib.request.urlretrieve(url, p)
        print("  %s bytes" % p.stat().st_size)


def static_400(path):
    """가변 폰트면 wght=400 으로 고정한 사본 경로를 돌려준다."""
    f = TTFont(str(path))
    if "fvar" not in f:
        return path
    out = FONTS / (path.stem.split("[")[0] + "-400.ttf")
    if not out.exists():
        instantiateVariableFont(f, {"wght": 400}, inplace=True, updateFontNames=False)
        f.save(str(out))
        print("가변 -> 고정:", out.name)
    return out


def needed_chars():
    """게임이 그릴 수 있는 글자 전부.

    Localization.cs 는 주석까지 통째로 넣는다(원래 그래 왔고, 몇 글자 더 들어가도 손해가 없다).
    나머지 .cs 는 문자열 리터럴만 — 주석까지 넣으면 폰트가 쓸데없이 커진다.
    """
    text = LOC.read_text(encoding="utf-8") + EXTRA
    lit = re.compile(r'"((?:[^"\\\n]|\\.)*)"')
    for f in sorted(SRC.glob("*.cs")):
        if f.name == LOC.name:
            continue
        for m in lit.finditer(f.read_text(encoding="utf-8")):
            text += m.group(1)
    # round45 버그: `> 0x20` 이라 공백(U+0020)이 빠졌다. 공백 글리프가 없으면 IMGUI 가 거기서 줄을
    #   끊어버려 "새로 시작" 이 "새로" 로만 보인다. 탭/개행만 빼고 공백은 반드시 넣는다.
    return sorted(set(ch for ch in text if ord(ch) >= 0x20))


ensure_sources()
CHAIN = [static_400(FONTS / name) for name, _ in SOURCES]

need = needed_chars()
print("필요한 글자 %d 자" % len(need))
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
    run([sys.executable, "-m", "fontTools.subset", str(src), f"--output-file={out}", f"--text-file={txt}",
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
    run([sys.executable, "-m", "fontTools.merge", *[str(p) for p in parts], f"--output-file={merged}"])

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
