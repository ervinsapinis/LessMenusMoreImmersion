import re
import sys
from pathlib import Path
import xml.etree.ElementTree as ET


def xml_escape_attr(s: str) -> str:
    return (
        s.replace("&", "&amp;")
        .replace("\"", "&quot;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
    )


def main() -> int:
    repo_root = Path(__file__).resolve().parents[1]
    xml_path = repo_root / "_Module" / "ModuleData" / "Languages" / "std_module_strings_xml.xml"

    # IDs referenced in code like {=lmmi_dirs_ask}
    id_re = re.compile(r"\{=([A-Za-z0-9_]+)\}")

    # Extract string-literal occurrences that contain {=ID} followed by some text.
    # NOTE: This is a heuristic; it won't catch texts constructed dynamically.
    # We only use it to seed English defaults.
    # Matches e.g. "{=abc}Hello world" (with any escaping already handled by C#)
    id_text_re = re.compile(r"\{=([A-Za-z0-9_]+)\}([^\"]*)")

    referenced_ids: set[str] = set()
    extracted_text: dict[str, str] = {}

    for cs in repo_root.rglob("*.cs"):
        s = cs.read_text(encoding="utf-8", errors="ignore")
        referenced_ids.update(id_re.findall(s))

        # Find IDs with inline text in C# string literals.
        for m in re.finditer(r"\"([^\"]*\{=[A-Za-z0-9_]+\}[^\"]*)\"", s):
            lit = m.group(1)
            m2 = id_text_re.search(lit)
            if not m2:
                continue
            _id = m2.group(1)
            text = m2.group(2)
            # Skip if empty; these are often "{=id}{VAR}" wrappers.
            # We'll handle some common wrappers below.
            if _id not in extracted_text and text.strip():
                extracted_text[_id] = text

    tree = ET.parse(xml_path)
    xml_ids = {e.attrib.get("id") for e in tree.getroot().findall(".//string") if e.attrib.get("id")}
    missing = sorted(referenced_ids - xml_ids)

    print(f"Referenced IDs in code: {len(referenced_ids)}")
    print(f"IDs already in XML    : {len(xml_ids)}")
    print(f"Missing IDs           : {len(missing)}")
    print("\n-- Missing IDs --")
    for _id in missing:
        print(_id)

    print("\n-- Suggested <string> entries (English defaults) --")
    for _id in missing:
        text = extracted_text.get(_id, "")
        # Common pattern: keys used only to wrap a variable.
        if not text:
            # If the ID is used like "{=id}{VAR}" we can't easily infer VAR here.
            # Leave blank so a human can fill it in.
            text = ""
        print(f"<string id=\"{_id}\" text=\"{xml_escape_attr(text)}\" />")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
