"""Hook de MkDocs: cada src/<Extensión>/README.md es la página de su extensión en el sitio.

Antes de compilar copia los README a docs/extensiones/<Nombre>.md y CONTRIBUTING.md a
docs/contribuir.md, y agrega la sección Extensiones al menú. Así una extensión nueva aparece
en el sitio sin tocar mkdocs.yml.
"""
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
DOCS = ROOT / "docs"
EXTENSIONES = DOCS / "extensiones"
REPO = "https://github.com/BruCascardo/gx-extensiones/blob/main/"

# Enlaces relativos de un README ("deploy.ps1", "../GxQuickVar/README.md") que en el sitio no existen
LINK = re.compile(r"\]\((?!https?://|#|mailto:)([^)\s]+)\)")


def _extensions():
    return sorted(p.parent for p in SRC.glob("*/README.md"))


def _fix_links(text, folder):
    def replace(match):
        target = match.group(1)
        name = Path(target).parent.name
        if target.endswith("README.md") and name.startswith("Gx"):
            return f"]({name}.md)"
        return f"]({REPO}src/{folder}/{target})"
    return LINK.sub(replace, text)


def on_config(config):
    EXTENSIONES.mkdir(exist_ok=True)
    for old in EXTENSIONES.glob("*.md"):
        old.unlink()
    pages = []
    for folder in _extensions():
        text = (folder / "README.md").read_text(encoding="utf-8-sig")
        (EXTENSIONES / f"{folder.name}.md").write_text(_fix_links(text, folder.name), encoding="utf-8")
        pages.append({folder.name: f"extensiones/{folder.name}.md"})

    contributing = (ROOT / "CONTRIBUTING.md").read_text(encoding="utf-8-sig")
    (DOCS / "contribuir.md").write_text(contributing.replace("](README.md)", "](index.md)"), encoding="utf-8")

    nav = config["nav"]
    position = next((i for i, item in enumerate(nav) if "Contribuir" in item), len(nav))
    nav.insert(position, {"Extensiones": pages})
    return config
