"""Reads specs/*/requirements.md headers and prints a Markdown SLA report; exit code 1 if anything is overdue."""
import datetime as dt, pathlib, re, sys
today = dt.date.today()
rows, overdue = [], 0
for req in sorted(pathlib.Path("specs").glob("*/requirements.md")):
    text = req.read_text(encoding="utf-8")
    def field(name):
        m = re.search(rf"^{name}:\s*(.+)$", text, re.M); return m.group(1).strip() if m else ""
    created, due, status, owner = field("Created"), field("Due"), field("Status") or "draft", field("Owner") or "-"
    if not created: rows.append((req.parent.name, "no header", "", status, owner, "add Created/Due/Status header")); continue
    c = dt.date.fromisoformat(created); d = dt.date.fromisoformat(due) if due else c + dt.timedelta(days=20)
    left = (d - today).days
    if status == "done": flag = "done"
    elif left < 0: flag = f"OVERDUE by {-left} d"; overdue += 1
    elif left <= 5: flag = f"due in {left} d"
    else: flag = f"{left} d left"
    rows.append((req.parent.name, created, d.isoformat(), status, owner, flag))
print(f"# Story SLA report ({today})\n\nRule: a story is done within 20 days of creation (standards/delivery-policy.md).\n")
print("| Feature | Created | Due | Status | Owner | State |\n|---|---|---|---|---|---|")
for r in rows: print("| " + " | ".join(r) + " |")
sys.exit(1 if overdue else 0)
