# 전체 테스트 실행. 사용: python tests/run_all.py [--only 이름,이름] [--skip-build]
import argparse, glob, os, subprocess, sys

TESTS = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(TESTS)
BUILD = os.path.join(TESTS, ".build", "server")
OUT = os.path.join(TESTS, ".out")
DB = os.path.join(ROOT, "Server", "resources", "db", "Pokemon.db")
CODE = os.path.join(ROOT, "Server")

ap = argparse.ArgumentParser()
ap.add_argument("--only", default="", help="실행할 묶음 이름(쉼표로 구분). 예: attack,field")
ap.add_argument("--skip-build", action="store_true", help="서버를 다시 빌드하지 않는다")
args = ap.parse_args()

if not args.skip_build:
    r = subprocess.run(["dotnet", "build", "Server", "-o", BUILD, "-v", "q"], cwd=ROOT)
    if r.returncode != 0:
        sys.exit("빌드 실패")

exe = os.path.join(BUILD, "Server.exe")
only = {n.strip() for n in args.only.split(",") if n.strip()}
suites = sorted(glob.glob(os.path.join(TESTS, "suites", "*_test.py")))
os.makedirs(OUT, exist_ok=True)

names = {os.path.basename(p)[:-3] for p in suites}
unknown = [n for n in only if n not in names and n + "_test" not in names]
if unknown:
    sys.exit("없는 묶음: " + ", ".join(sorted(unknown)) + " (있는 묶음: " + ", ".join(sorted(n.removesuffix("_test") for n in names)) + ")")

failed = 0
for path in suites:
    name = os.path.basename(path)[:-3]
    if only and name.removesuffix("_test") not in only and name not in only:
        continue
    out_path = os.path.join(OUT, name + ".txt")
    if os.path.exists(out_path):
        os.remove(out_path)   # 이전 실행의 결과가 남아 통과로 보이지 않게 한다
    note = ""
    try:
        r = subprocess.run([sys.executable, "-I", path, exe, DB, out_path, CODE], timeout=15 * 60)
        if r.returncode != 0:
            note = f" (종료 코드 {r.returncode})"
        code_ok = r.returncode == 0
    except subprocess.TimeoutExpired:
        code_ok = False
        note = " (15분 초과)"
    lines = open(out_path, encoding="utf-8").read().splitlines() if os.path.exists(out_path) else []
    n_pass = sum(l.startswith("PASS ") for l in lines)
    n_fail = sum(l.startswith("FAIL ") for l in lines)
    if not lines:
        note += " (결과 파일 없음)"
    elif n_pass + n_fail == 0:
        note += " (PASS/FAIL 줄 없음)"
    print(f"{name} PASS {n_pass} FAIL {n_fail}{note}", flush=True)
    if not code_ok or n_fail or n_pass + n_fail == 0:
        failed += 1

print("전체: 통과" if not failed else f"전체: 실패 {failed}개 묶음")
sys.exit(1 if failed else 0)
