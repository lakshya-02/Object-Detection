# IEEE paper source

Upload `Team4_QuestVisionAssistant_Source.zip` to a new Overleaf project, then set `Team4_QuestVisionAssistant_Final.tex` as the main file. The supplied `IEEEtran.cls` is included. Compile with pdfLaTeX (or XeLaTeX) and recompile once so citations resolve.

The included `Team4_QuestVisionAssistant_Final.pdf` compiled locally to five pages. Overleaf may typeset slightly differently, so confirm its final page count and inspect all table and figure placement. Run the institution's similarity check before submission.

`figures/block_diagram.drawio` and `figures/working_flow.drawio` are editable sources for the vector diagrams. The three charts are generated from saved test XML and measured file sizes by `make_figures.py`. The test outputs are under `evidence/`. The public GitHub repository linked in the paper contains the source code; generated builds, caches, and credentials are excluded.

The paper distinguishes manually observed Quest interactions from offline tests. It does not claim detector accuracy, FPS, power, or a smart glasses deployment. The current platform is Meta Quest 3; smart glasses are future work.
