from pathlib import Path

old = '[0].Replace(".", "")[:30]'
new = '[0].Replace(".", "")[..30]'
for p in Path("/home/jawarneh/Channeled").rglob("*.cs"):
    t = p.read_text()
    if old in t:
        p.write_text(t.replace(old, new))
        print(p)
