"""소개 사이트(site/index.html) FAQ를 웹 설정 화면(wwwroot/index.html)의 FAQ_DATA로 옮긴다.

사이트 FAQ가 원본이다. 사이트 질문을 고친 뒤 `python3 tools/faq_sync.py`를 실행하면 웹 FAQ 탭이 같아진다.
`--check`는 바꾸지 않고 다르면 1로 끝난다(시험·커밋 전 확인용).
"""
import json, re, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SITE = ROOT / "site" / "index.html"
WEB = ROOT / "src" / "Gravity.Server" / "wwwroot" / "index.html"
START, END = "/*FAQ_DATA_START*/", "/*FAQ_DATA_END*/"
PAIR = re.compile(r'<span class="ko">(.*?)</span><span class="en" lang="en">(.*?)</span>', re.S)


def pick(html: str, lang: str) -> str:
    out = PAIR.sub(lambda m: m.group(1 if lang == "ko" else 2), html)
    return re.sub(r"\s*\n\s*", "", out).strip()


def extract() -> list:
    s = SITE.read_text(encoding="utf-8")
    i = s.index('<div class="faq">')
    f = s[i:s.index("</section>", i)]
    groups = []
    for part in re.split(r'(?=<h3 class="faq-group")', f)[1:]:
        g = re.match(r'<h3 class="faq-group"[^>]*>(.*?)</h3>', part, re.S).group(1)
        items = []
        for qid, inner in re.findall(r'<details id="([^"]+)">(.*?)</details>', part, re.S):
            summary, body = inner.split("</summary>", 1)
            summary = summary.split("<summary>", 1)[1]
            items.append([qid, pick(summary, "ko"), pick(summary, "en"), pick(body, "ko"), pick(body, "en")])
        groups.append([pick(g, "ko"), pick(g, "en"), items])
    return groups


def main() -> int:
    data = START + "const FAQ_DATA=" + json.dumps(extract(), ensure_ascii=False, separators=(",", ":")) + ";" + END
    web = WEB.read_text(encoding="utf-8")
    a, b = web.index(START), web.index(END) + len(END)
    if web[a:b] == data:
        print("웹 FAQ가 사이트와 같습니다.")
        return 0
    if "--check" in sys.argv:
        print("웹 FAQ가 사이트와 다릅니다: python3 tools/faq_sync.py 를 실행하세요.")
        return 1
    WEB.write_text(web[:a] + data + web[b:], encoding="utf-8")
    print(f"웹 FAQ를 사이트 FAQ {sum(len(g[2]) for g in extract())}개로 맞췄습니다.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
