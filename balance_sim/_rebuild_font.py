"""
PopCellKR.ttf 재생성 — Localization.cs 전체 텍스트 기준으로 KR 베이스를 서브셋하고,
KR에 없는 문자(대개 일본어 전용 가나/신자체 한자)만 자동으로 찾아서 JP 폰트에서 패치.
매번 수동으로 빠진 글자를 찾지 않아도 되게 전체 파이프라인을 한 스크립트로.
"""
import pathlib, subprocess, sys
from fontTools.ttLib import TTFont

ROOT = pathlib.Path(__file__).resolve().parent.parent
LOC = ROOT / "My project" / "Assets" / "Scripts" / "BlackholeGame" / "Localization.cs"
OUT = ROOT / "My project" / "Assets" / "Resources" / "PopCellKR.ttf"
SCRATCH = pathlib.Path(r"C:\Users\Administrator\Desktop\AutoClickerGame\balance_sim")
KR_SRC = pathlib.Path(r"C:\Users\ADMINI~1\AppData\Local\Temp\claude\C--Users-Administrator-Desktop-AutoClickerGame\a9375838-d49f-4434-8fb5-09e8dacd67a7\scratchpad\NotoSansKR-Regular.ttf")
JP_SRC = pathlib.Path(r"C:\Users\ADMINI~1\AppData\Local\Temp\claude\C--Users-Administrator-Desktop-AutoClickerGame\a9375838-d49f-4434-8fb5-09e8dacd67a7\scratchpad\NotoSansJP-Regular.ttf")

kr_base = SCRATCH / "_kr_base.ttf"
jp_patch = SCRATCH / "_jp_patch.ttf"
merged = SCRATCH / "_merged.ttf"

def run(cmd):
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0:
        print("FAILED:", cmd, "\n", r.stdout, r.stderr)
        sys.exit(1)

# 1) KR 베이스를 현재 전체 파일 기준으로 서브셋
run(["python", "-m", "fontTools.subset", str(KR_SRC),
     f"--output-file={kr_base}", f"--text-file={LOC}", "--glyph-names"])

# 2) KR 베이스에서 빠진 문자 찾기
f = TTFont(str(kr_base))
cmap = f.getBestCmap()
text = LOC.read_text(encoding="utf-8")
need = set(ch for ch in text if ord(ch) > 0x7f)
missing = sorted(c for c in need if ord(c) not in cmap)
print("KR 서브셋에서 빠진 문자(JP 패치 대상):", len(missing), "".join(missing))

if missing:
    miss_file = SCRATCH / "_jp_missing.txt"
    miss_file.write_text("".join(missing), encoding="utf-8")
    run(["python", "-m", "fontTools.subset", str(JP_SRC),
         f"--output-file={jp_patch}", f"--text-file={miss_file}", "--glyph-names"])
    run(["python", "-m", "fontTools.merge", str(kr_base), str(jp_patch), f"--output-file={merged}"])
else:
    merged = kr_base

# 3) 최종 검증
ff = TTFont(str(merged))
cmap2 = ff.getBestCmap()
still_missing = sorted(c for c in need if ord(c) not in cmap2)
print("최종 결손:", len(still_missing), "".join(still_missing))
if still_missing:
    print("!!! 여전히 빠진 문자 있음 — JP 폰트에도 없는 글자. 수동 확인 필요.")
    sys.exit(1)

import shutil
shutil.copy(str(merged), str(OUT))
print("완료 ->", OUT, OUT.stat().st_size, "bytes, glyphs:", ff["maxp"].numGlyphs)

for p in [kr_base, jp_patch, merged, SCRATCH / "_jp_missing.txt"]:
    try:
        if p.exists() and p != OUT:
            p.unlink()
    except Exception:
        pass
