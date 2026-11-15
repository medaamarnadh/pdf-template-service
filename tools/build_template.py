"""
Stand-in for the forms designer (Acrobat). Builds the static template PDF with
100+ uniquely named AcroForm fields plus the mapping JSON (key = data key,
value = PDF field id). In production the template is authored in Acrobat.
Form: fictional "Small Business Growth Grant Application".
"""
import json, os
from reportlab.pdfgen import canvas
from reportlab.lib.pagesizes import letter
from reportlab.lib import colors

OUT_DIR = os.path.join(os.path.dirname(__file__), "..", "src", "PdfFill.Api", "Templates")
NAME = "grant_application"
PDF = os.path.join(OUT_DIR, f"{NAME}.pdf")
MAP = os.path.join(OUT_DIR, f"{NAME}.mapping.json")

W, H = letter
c = canvas.Canvas(PDF, pagesize=letter)
c.setTitle("Small Business Growth Grant Application (POC template)")
f = c.acroForm
mapping = {}
NAVY = colors.HexColor("#0b3d5c")

def add_map(key, fid):
    cur = mapping.get(key)
    mapping[key] = fid if cur is None else (cur + [fid] if isinstance(cur, list) else [cur, fid])

def text(fid, tip, x, y, w, h=16, size=9, multiline=False, key=None):
    add_map(key or fid.lower(), fid)
    f.textfield(name=fid, tooltip=tip, x=x, y=y, width=w, height=h, borderStyle="solid",
                borderWidth=0.5, borderColor=colors.grey, fillColor=colors.white, fontSize=size,
                fieldFlags="multiline" if multiline else "")

def check(fid, tip, x, y, size=12):
    add_map(fid.lower(), fid)
    f.checkbox(name=fid, tooltip=tip, x=x, y=y, size=size, buttonStyle="check",
               borderWidth=0.5, borderColor=colors.grey, fillColor=colors.white)

def radio(fid, tip, x, y, values, gap=52):
    add_map(fid.lower(), fid)
    for i, v in enumerate(values):
        f.radio(name=fid, tooltip=f"{tip}: {v}", value=v, x=x + i*gap, y=y, size=12,
                borderWidth=0.5, borderColor=colors.grey, fillColor=colors.white, shape="circle")
        c.setFont("Helvetica", 8); c.drawString(x + i*gap + 15, y + 3, v)

def label(x, y, s, size=9, bold=False, color=colors.black):
    c.setFillColor(color); c.setFont("Helvetica-Bold" if bold else "Helvetica", size)
    c.drawString(x, y, s); c.setFillColor(colors.black)

def rule(y, title):
    label(36, y, title.upper(), 9, True, NAVY)
    c.setStrokeColor(NAVY); c.line(36, y-3, W-36, y-3); c.setStrokeColor(colors.black)

def header(page):
    sfx = "" if page == 1 else f"_P{page}"
    c.setFillColor(NAVY); c.rect(0, H-28, W, 28, stroke=0, fill=1); c.setFillColor(colors.white)
    c.setFont("Helvetica-Bold", 11); c.drawString(36, H-19, "Small Business Growth Grant Program")
    c.setFillColor(colors.black)
    label(36, H-46, "Applicant:", 8.5, True); text(f"Applicant_Name{sfx}", "Applicant business name", 90, H-50, 220, 15, 9, key="applicant_name")
    label(380, H-46, "Application #:", 8.5, True); text(f"Application_No{sfx}", "Application number", 450, H-50, 120, 15, 9, key="application_no")
    c.setFont("Helvetica", 7); c.drawRightString(W-36, 24, f"Grant Application - Page {page} of 3")

# ---------------- Page 1: applicant profile ----------------
header(1)
rule(H-80, "Section 1 - Applicant information")
label(36, H-100, "Legal business name:"); text("Business_Name", "Legal business name", 140, H-104, 250)
label(400, H-100, "EIN:"); text("Ein", "Employer identification number", 425, H-104, 145)
label(36, H-124, "Street address:"); text("Address", "Street address", 140, H-128, 250)
label(400, H-124, "Year established:"); text("Year_Established", "Year established", 490, H-128, 80)
label(36, H-148, "City / State / ZIP:"); text("City", "City", 140, H-152, 140); text("State", "State", 285, H-152, 40); text("Zip", "ZIP code", 330, H-152, 60)
label(400, H-148, "Website:"); text("Website", "Website", 445, H-152, 125)
label(36, H-172, "Primary contact:"); text("Contact_Name", "Primary contact name", 140, H-176, 150)
label(300, H-172, "Title:"); text("Contact_Title", "Primary contact title", 330, H-176, 100)
label(440, H-172, "Phone:"); text("Phone", "Phone", 475, H-176, 95)
label(36, H-196, "Email:"); text("Email", "Email address", 140, H-200, 250)
label(400, H-196, "NAICS code:"); text("Naics", "NAICS code", 460, H-200, 110)

label(36, H-224, "Entity type:", 9, True)
for i, (fid, lbl) in enumerate([("Entity_Llc","LLC"),("Entity_Corp","Corporation"),("Entity_SoleProp","Sole proprietor"),("Entity_Partnership","Partnership"),("Entity_Nonprofit","Nonprofit")]):
    check(fid, f"Entity type: {lbl}", 100 + i*95, H-228); label(116 + i*95, H-225, lbl, 8)
label(36, H-250, "Minority-owned:", 8.5); radio("Minority_Owned", "Minority-owned business", 115, H-253, ["Yes","No"])
label(230, H-250, "Veteran-owned:", 8.5); radio("Veteran_Owned", "Veteran-owned business", 305, H-253, ["Yes","No"])
label(420, H-250, "Rural location:", 8.5); radio("Rural_Location", "Located in a rural area", 490, H-253, ["Yes","No"], gap=40)
label(36, H-274, "Full-time employees:"); text("Employees_Ft", "Full-time employees", 140, H-278, 50)
label(210, H-274, "Part-time employees:"); text("Employees_Pt", "Part-time employees", 315, H-278, 50)
label(390, H-274, "Jobs to be created:"); text("Jobs_Created", "Jobs to be created", 490, H-278, 50)

rule(H-310, "Section 2 - Revenue history (quarterly, USD)")
cols = ["Q1","Q2","Q3","Q4","Annual total"]
x0, cw = 150, 84
label(40, H-330, "Fiscal year", 8, True)
for i, col in enumerate(cols): label(x0 + i*cw + 4, H-330, col, 8, True)
for r, fy in enumerate(["FY1","FY2","FY3"]):
    yy = H-336 - (r+1)*18
    text(f"Rev_{fy}_Label", f"Revenue row {r+1} fiscal year", 40, yy, 100, 16, 9)
    for i, col in enumerate(cols):
        fid = f"Rev_{fy}_{'Total' if i==4 else col}"
        text(fid, f"Revenue row {r+1} {col}", x0 + i*cw, yy, cw, 16, 9)
c.setStrokeColor(colors.lightgrey); c.rect(40, H-336-3*18, 110 + 5*cw, 3*18); c.setStrokeColor(colors.black)
label(36, H-410, "Average annual growth (%):"); text("Growth_Pct", "Average annual growth percent", 170, H-414, 60)

rule(H-445, "Section 3 - Project description")
label(36, H-465, "Project title:"); text("Project_Title", "Project title", 110, H-469, 300)
label(420, H-465, "Type:"); radio("Project_Type", "Project type", 450, H-469, ["New","Expansion"], gap=60)
label(36, H-489, "Start date:"); text("Project_Start", "Project start date", 110, H-493, 90)
label(220, H-489, "End date:"); text("Project_End", "Project end date", 270, H-493, 90)
label(380, H-489, "Duration (months):"); text("Project_Months", "Project duration in months", 475, H-493, 50)
label(36, H-513, "Project summary:")
text("Project_Summary", "Project summary", 36, H-590, W-72, 72, 9, multiline=True)
label(36, H-612, "Expected community impact:")
text("Project_Impact", "Expected community impact", 36, H-680, W-72, 62, 9, multiline=True)
c.showPage()

# ---------------- Page 2: budget ----------------
header(2)
rule(H-80, "Section 4 - Proposed budget (USD)")
bcols = [("Category", 150), ("Requested", 90), ("Match", 90), ("Justification", 210)]
x = 36
for name, w in bcols: label(x+3, H-98, name, 8, True); x += w
for r in range(1, 9):
    x = 36; yy = H-100 - r*18
    for name, w in bcols:
        text(f"Budget{r}_{name}", f"Budget line {r} {name.lower()}", x, yy, w, 16, 8); x += w
c.setStrokeColor(colors.lightgrey); c.rect(36, H-100-8*18, sum(w for _, w in bcols), 8*18); c.setStrokeColor(colors.black)
label(36, H-268, "Total requested:", 9, True); text("Total_Requested", "Total amount requested", 130, H-272, 90, 16, 9)
label(240, H-268, "Total match:", 9, True); text("Total_Match", "Total matching funds", 305, H-272, 90, 16, 9)
label(410, H-268, "Match ratio (%):", 9, True); text("Match_Pct", "Match percentage", 495, H-272, 60, 16, 9)

rule(H-305, "Section 5 - Funding sources")
label(36, H-325, "Source", 8, True); label(300, H-325, "Amount", 8, True); label(400, H-325, "Secured?", 8, True)
for r in range(1, 5):
    yy = H-328 - r*18
    text(f"Source{r}_Name", f"Funding source {r} name", 36, yy, 255, 16, 8)
    text(f"Source{r}_Amount", f"Funding source {r} amount", 296, yy, 95, 16, 8)
    radio(f"Source{r}_Secured", f"Funding source {r} secured", 405, yy+2, ["Yes","No"], gap=45)

rule(H-430, "Section 6 - Compliance checklist")
items = [("Chk_Registered", "Business is registered and in good standing with the state"),
         ("Chk_Taxes", "All federal and state tax filings are current"),
         ("Chk_NoDebarment", "Applicant is not debarred or suspended from public funding"),
         ("Chk_Insurance", "General liability insurance is in force"),
         ("Chk_Audit", "Applicant agrees to a post-award financial review"),
         ("Chk_Reporting", "Applicant agrees to quarterly progress reporting")]
for i, (fid, lbl) in enumerate(items):
    yy = H-455 - i*20
    check(fid, lbl, 40, yy); label(58, yy+3, lbl, 8.5)
label(36, H-590, "Reviewer notes (office use):", 8.5); text("Reviewer_Notes", "Reviewer notes", 36, H-650, W-72, 56, 8, multiline=True)
label(36, H-672, "Score (0-100):"); text("Review_Score", "Review score", 110, H-676, 50)
label(200, H-672, "Recommendation:"); radio("Review_Decision", "Review recommendation", 290, H-676, ["Approve","Defer","Decline"], gap=70)
c.showPage()

# ---------------- Page 3: certification & signatures ----------------
header(3)
rule(H-80, "Section 7 - Certification and signatures")
c.setFont("Helvetica", 8.5)
c.drawString(36, H-98, "The undersigned certify that the information provided in this application is true and complete to the best of their knowledge.")
def sig(fid, title, y):
    label(36, y+50, title, 8.5, True)
    c.setStrokeColor(colors.grey); c.setDash(3, 3); c.rect(36, y, 260, 44); c.setDash(); c.setStrokeColor(colors.black)
    text(fid, f"{title} signature placeholder", 36, y, 260, 44, 8)   # image stamped here by the filler
    label(320, y+30, "Printed name:", 8.5); text(f"{fid}_Name", f"{title} printed name", 390, y+26, 170, 14, 9)
    label(320, y+8, "Date:", 8.5); text(f"{fid}_Date", f"{title} signature date", 390, y+4, 100, 14, 9)
sig("Sig_Applicant", "Authorized applicant representative", H-170)
sig("Sig_Preparer", "Application preparer", H-260)
sig("Sig_Reviewer", "Program reviewer", H-350)
c.save()

with open(MAP, "w") as fh:
    json.dump(dict(sorted(mapping.items())), fh, indent=2)
print(f"template: {len(mapping)} keys -> {PDF}")
