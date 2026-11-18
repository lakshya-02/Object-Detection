"""Make editable draw.io schematics and vector charts from recorded project evidence."""

from pathlib import Path
from xml.etree import ElementTree as ET
import xml.etree.ElementTree as XML

from reportlab.lib.colors import HexColor
from reportlab.pdfgen import canvas


ROOT = Path(__file__).parent
FIGURES = ROOT / "figures"
FIGURES.mkdir(exist_ok=True)
INK = HexColor("#18324b")
PALE = HexColor("#eaf1f8")
ACCENT = HexColor("#3974ac")


def diagram(name, width, height, nodes, edges):
    mxfile = ET.Element("mxfile", host="app.diagrams.net", type="device")
    page = ET.SubElement(mxfile, "diagram", name=name, id=name.replace(" ", "-"))
    graph = ET.SubElement(page, "mxGraphModel", dx="800", dy="500", grid="1",
                          gridSize="10", page="1", pageScale="1", pageWidth=str(width),
                          pageHeight=str(height))
    cells = ET.SubElement(graph, "root")
    ET.SubElement(cells, "mxCell", id="0")
    ET.SubElement(cells, "mxCell", id="1", parent="0")
    by_id = {node[0]: node for node in nodes}
    for ident, label, x, y, w, h in nodes:
        cell = ET.SubElement(cells, "mxCell", id=ident, value=label.replace("\n", "<br>"),
                             style="rounded=1;whiteSpace=wrap;html=1;fillColor=#EAF1F8;"
                                   "strokeColor=#3974AC;fontColor=#18324B;fontSize=12;",
                             vertex="1", parent="1")
        ET.SubElement(cell, "mxGeometry", x=str(x), y=str(y), width=str(w),
                      height=str(h), attrib={"as": "geometry"})
    for index, (source, target) in enumerate(edges):
        cell = ET.SubElement(cells, "mxCell", id=f"edge{index}", source=source,
                             target=target, style="edgeStyle=orthogonalEdgeStyle;"
                             "endArrow=block;strokeColor=#3974AC;strokeWidth=2;",
                             edge="1", parent="1")
        ET.SubElement(cell, "mxGeometry", relative="1", attrib={"as": "geometry"})
    ET.ElementTree(mxfile).write(FIGURES / f"{name}.drawio", encoding="utf-8", xml_declaration=True)

    scale = 540 / width
    pdf = canvas.Canvas(str(FIGURES / f"{name}.pdf"), pagesize=(540, height * scale))
    pdf.setLineWidth(1.2)
    for source, target in edges:
        a, b = by_id[source], by_id[target]
        ax, ay = a[2] + a[4] / 2, a[3] + a[5] / 2
        bx, by = b[2] + b[4] / 2, b[3] + b[5] / 2
        if abs(bx - ax) >= abs(by - ay):
            ax += (a[4] / 2) * (1 if bx > ax else -1)
            bx -= (b[4] / 2) * (1 if bx > ax else -1)
        else:
            ay += (a[5] / 2) * (1 if by > ay else -1)
            by -= (b[5] / 2) * (1 if by > ay else -1)
        x1, y1, x2, y2 = ax * scale, (height - ay) * scale, bx * scale, (height - by) * scale
        pdf.setStrokeColor(ACCENT)
        pdf.line(x1, y1, x2, y2)
        # Small filled arrowhead at the target.
        dx, dy = x2 - x1, y2 - y1
        length = max((dx * dx + dy * dy) ** 0.5, 1)
        ux, uy = dx / length, dy / length
        path = pdf.beginPath()
        path.moveTo(x2, y2)
        path.lineTo(x2 - 6 * ux - 3 * uy, y2 - 6 * uy + 3 * ux)
        path.lineTo(x2 - 6 * ux + 3 * uy, y2 - 6 * uy - 3 * ux)
        path.close()
        pdf.setFillColor(ACCENT)
        pdf.drawPath(path, fill=1, stroke=0)
    for _, label, x, y, w, h in nodes:
        px, py = x * scale, (height - y - h) * scale
        pdf.setFillColor(PALE)
        pdf.setStrokeColor(ACCENT)
        pdf.roundRect(px, py, w * scale, h * scale, 5, fill=1, stroke=1)
        pdf.setFillColor(INK)
        lines = label.split("\n")
        pdf.setFont("Helvetica-Bold", 8)
        for i, line in enumerate(lines):
            pdf.drawCentredString(px + w * scale / 2, py + h * scale / 2 +
                                  (len(lines) - 1 - 2 * i) * 5 - 3, line)
    pdf.save()


def bars(name, title, rows, maximum, unit):
    width, height = 252, 138
    pdf = canvas.Canvas(str(FIGURES / f"{name}.pdf"), pagesize=(width, height))
    pdf.setFillColor(INK)
    pdf.setFont("Helvetica-Bold", 9)
    pdf.drawString(8, height - 15, title)
    label_width, plot_width = 108, 95
    for index, (label, value) in enumerate(rows):
        y = height - 35 - index * (95 / max(len(rows), 1))
        pdf.setFont("Helvetica", 7)
        pdf.drawRightString(label_width, y + 2, label)
        pdf.setFillColor(PALE)
        pdf.rect(label_width + 5, y - 1, plot_width, 8, fill=1, stroke=0)
        pdf.setFillColor(ACCENT)
        pdf.rect(label_width + 5, y - 1, plot_width * value / maximum, 8, fill=1, stroke=0)
        pdf.setFillColor(INK)
        pdf.drawString(label_width + plot_width + 8, y + 1, f"{value:g}{unit}")
    pdf.save()


diagram("block_diagram", 900, 230, [
    ("camera", "Quest 3\ncamera", 12, 20, 120, 48),
    ("model", "YOLO12n\nSentis", 182, 20, 120, 48),
    ("scene", "Grounded\nscene JSON", 352, 20, 120, 48),
    ("router", "Question\nrouter", 522, 20, 120, 48),
    ("panel", "Text and\nElevenLabs TTS", 692, 20, 180, 48),
    ("microphone", "Quest 3\nmicrophone", 12, 150, 120, 48),
    ("gateway", "PC gateway\nkeys remain here", 182, 150, 120, 48),
    ("stt", "ElevenLabs\nSTT", 352, 150, 120, 48),
    ("reason", "Google AI\nvia gateway", 522, 150, 120, 48),
    ("validate", "Index and\naction check", 692, 150, 180, 48),
], [("camera", "model"), ("model", "scene"), ("scene", "router"),
    ("router", "panel"), ("microphone", "gateway"), ("gateway", "stt"),
    ("stt", "router"), ("router", "reason"), ("reason", "validate"),
    ("validate", "panel")])

diagram("working_flow", 900, 250, [
    ("start", "X: STT or\nY: inventory", 10, 20, 130, 50),
    ("capture", "Fresh camera\nsnapshot", 160, 20, 130, 50),
    ("detect", "Detect, count,\ncolor and zone", 310, 20, 140, 50),
    ("question", "Classify\nquestion", 470, 20, 130, 50),
    ("local", "Local grounded\nanswer", 630, 20, 125, 50),
    ("output", "Text and\nspoken audio", 770, 20, 120, 50),
    ("gateway", "Google AI via\nPC gateway", 470, 165, 130, 50),
    ("check", "Validate model\naction indices", 630, 165, 125, 50),
    ("fallback", "Unsupported:\nspeak uncertainty", 770, 165, 120, 50),
], [("start", "capture"), ("capture", "detect"), ("detect", "question"),
    ("question", "local"), ("local", "output"), ("question", "gateway"),
    ("gateway", "check"), ("check", "output"), ("check", "fallback")])

xml = XML.parse(ROOT / "evidence" / "unity-editmode-results.xml")
fixtures = [(node.get("name"), int(node.get("passed")), float(node.get("duration")))
            for node in xml.findall(".//test-suite[@type='TestFixture']")]
bars("test_counts", "Edit Mode cases passed", [(name.replace("Tests", ""), count)
     for name, count, _ in fixtures], 32, "")
bars("test_times", "Test fixture duration (not device latency)",
     [("Model smoke", round(next(t for n, _, t in fixtures if n == "ModelInferenceSmokeTests"), 2)),
      ("Other fixtures", round(sum(t for n, _, t in fixtures if n != "ModelInferenceSmokeTests"), 2))],
     24, " s")
model_mb = (ROOT.parent / "Assets" / "MetaXR" / "yolo12nNoNMS.sentis").stat().st_size / 1_000_000
apk_mb = 98_578_068 / 1_000_000  # Measured final APK size in evidence/final-device-check.txt.
bars("artifact_sizes", "Artifact size (decimal MB)",
     [("Sentis model", round(model_mb, 2)), ("Quest APK", round(apk_mb, 2))], 100, " MB")
