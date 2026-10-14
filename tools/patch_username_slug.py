from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
files = list((root / "tests").rglob("*.cs")) + [root / "src/Channeled.Api/Program.cs"]
for path in files:
    if not path.exists():
        continue
    text = path.read_text()
    if "new AppUser" not in text:
        continue

    def repl(m: re.Match[str]) -> str:
        block = m.group(0)
        if "UsernameSlug" in block:
            return block
        name_m = re.search(r"DisplayName\s*=\s*[^,\n]+", block)
        if not name_m:
            return block
        email_m = re.search(r'Email\s*=\s*"([^"]+)"', block)
        if email_m:
            local = email_m.group(1).split("@")[0].replace(".", "")[:30]
            insert = f'\n                    UsernameSlug = "{local}",'
        elif re.search(r"Email\s*=\s*email", block):
            insert = '\n                    UsernameSlug = email.Split(\'@\')[0].Replace(".", "")[:30],'
        else:
            insert = '\n                    UsernameSlug = "u" + Guid.NewGuid().ToString("N")[..12],'
        return block[: name_m.end()] + "," + insert + block[name_m.end() :]

    new_text, n = re.subn(r"new AppUser\s*\{[^}]+\}", repl, text, flags=re.S)
    if n and new_text != text:
        path.write_text(new_text)
        print(f"patched {path} ({n})")
