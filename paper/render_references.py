"""Render verified metadata as compact IEEE-style bibitems in citation order."""

import json
import re
import unicodedata
from pathlib import Path


ROOT = Path(__file__).parent
metadata = json.loads((ROOT / "references_verified.json").read_text(encoding="utf-8"))
source = (ROOT / "TeamName_QuestVisionAssistant_Final.tex").read_text(encoding="utf-8")


def tex(value):
    value = unicodedata.normalize("NFKD", value).encode("ascii", "ignore").decode()
    for old, new in (("&", r"\&"), ("%", r"\%"), ("_", r"\_"), ("#", r"\#")):
        value = value.replace(old, new)
    return value


manual = {
    "meta": ("Meta", "Passthrough Camera API Overview for Unity", "2026",
             "https://developers.meta.com/vr/documentation/unity/unity-pca-overview/"),
    "unity": ("Unity Technologies", "Sentis 2.6.1 Overview", "2026",
              "https://docs.unity.cn/Packages/com.unity.ai.inference@2.6/manual/index.html"),
}

order = []
for group in re.findall(r"\\cite\{([^}]+)\}", source):
    for key in group.split(","):
        key = key.strip()
        if key not in order:
            order.append(key)

missing = set(metadata) | set(manual)
missing.difference_update(order)
unknown = set(order) - set(metadata) - set(manual)
if unknown or missing:
    raise SystemExit(f"Unknown citation keys: {sorted(unknown)}; unused references: {sorted(missing)}")

lines = []
for key in order:
    if key in manual:
        author, title, year, url = manual[key]
        lines.append(f"\\bibitem{{{key}}} {tex(author)}, ``{tex(title)},'' {year}. [Online]. Available: \\url{{{url}}}. Accessed: Nov. 18, 2026.")
        continue
    item = metadata[key]
    authors = item["authors"]
    author = tex("Gemini Team" if key == "gemini15" else authors[0]) + (" et al." if len(authors) > 1 else "")
    doi = item["doi"]
    venue = item["venue"]
    if doi.startswith("10.48550/arxiv."):
        venue = "arXiv preprint"
    elif "Computer Vision and Pattern Recognition" in venue:
        venue = "Proc. IEEE/CVF CVPR"
    elif "neural information processing systems" in venue.lower():
        venue = "Proc. NeurIPS"
    elif "Lecture notes in computer science" in venue.lower():
        venue = "Proc. ECCV"
    elif "Findings of" in venue:
        venue = "Findings of EMNLP"
    lines.append(f"\\bibitem{{{key}}} {author}, ``{tex(item['title'])},'' \\emph{{{tex(venue)}}}, {item['year']}, doi: \\url{{https://doi.org/{doi}}}.")

(ROOT / "references.tex").write_text("\n".join(lines) + "\n", encoding="utf-8")
recent = sum(2024 <= metadata[k]["year"] <= 2026 for k in metadata)
print(f"Rendered {len(order)} references; {recent}/{len(order)} academic papers are from 2024--2026.")
