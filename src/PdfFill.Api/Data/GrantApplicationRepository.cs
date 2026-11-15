using Dapper;

namespace PdfFill.Api.Data;

/// <summary>
/// Fetches one grant application from several tables and flattens it into a single
/// dictionary whose keys are exactly the keys used in the mapping JSON.
///   - Header columns: aliased in SQL to the mapping key where the DB name differs.
///   - Revenue rows:   rev_fy{n}_{column}   (rev_fy1_q1 ... rev_fy3_total)
///   - Budget / sources: arrays of row objects ("budget", "source") consumed by table mapping entries
///   - Compliance:     chk_{code}
///   - Signatures:     sig_{role} (PNG bytes), sig_{role}_name, sig_{role}_date
/// Grid rows that do not exist in the DB are emitted as "" so the PDF cells are blank.
/// </summary>
public sealed class GrantApplicationRepository(Database db)
{
    public async Task<Dictionary<string, object?>?> LoadAsync(int applicationId)
    {
        using var c = db.Open();
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // 1) Header: grant_applications + applicants + contacts
        var header = await c.QuerySingleOrDefaultAsync(
            """
            SELECT ap.business_name                     AS applicant_name,
                   ap.business_name, ap.ein, ap.address, ap.city, ap.state, ap.zip, ap.website, ap.naics,
                   ap.year_established, ap.employees_ft, ap.employees_pt,
                   ap.entity_type = 'LLC'               AS entity_llc,
                   ap.entity_type = 'CORP'              AS entity_corp,
                   ap.entity_type = 'SOLEPROP'          AS entity_soleprop,
                   ap.entity_type = 'PARTNERSHIP'       AS entity_partnership,
                   ap.entity_type = 'NONPROFIT'         AS entity_nonprofit,
                   CASE ap.minority_owned WHEN 1 THEN 'Yes' ELSE 'No' END AS minority_owned,
                   CASE ap.veteran_owned  WHEN 1 THEN 'Yes' ELSE 'No' END AS veteran_owned,
                   CASE ap.rural_location WHEN 1 THEN 'Yes' ELSE 'No' END AS rural_location,
                   ct.contact_name, ct.contact_title, ct.phone, ct.email,
                   a.application_no, a.project_title, a.project_type, a.project_start, a.project_end,
                   CAST((julianday(a.project_end) - julianday(a.project_start)) / 30.44 AS INTEGER) AS project_months,
                   a.jobs_created, a.project_summary, a.project_impact,
                   a.reviewer_notes, a.review_score, a.review_decision
            FROM grant_applications a
            JOIN applicants ap ON ap.applicant_id = a.applicant_id
            JOIN contacts   ct ON ct.applicant_id = ap.applicant_id
            WHERE a.application_id = @id
            """, new { id = applicationId });
        if (header is null) return null;
        Merge(data, header, "");

        // 2) Revenue grid (3 rows x 4 quarters + total + label)
        Blank(data, 3, "rev_fy{0}_", ["label", "q1", "q2", "q3", "q4", "total"]);
        var revenue = (await c.QueryAsync("SELECT * FROM revenue_quarters WHERE application_id = @id ORDER BY row_no", new { id = applicationId })).ToList();
        var annualTotals = new List<long>();
        foreach (var row in revenue)
        {
            var d = (IDictionary<string, object?>)row;
            var prefix = $"rev_fy{d["row_no"]}_";
            Merge(data, row, prefix, skip: new[] { "application_id", "row_no" });
            var total = new[] { "q1", "q2", "q3", "q4" }.Sum(q => Convert.ToInt64(d[q] ?? 0L));
            data[prefix + "total"] = total;
            if (new[] { "q1", "q2", "q3", "q4" }.All(q => d[q] is not null)) annualTotals.Add(total);   // complete years only
        }
        data["growth_pct"] = annualTotals.Count >= 2 && annualTotals[0] > 0
            ? Math.Round((Math.Pow((double)annualTotals[^1] / annualTotals[0], 1.0 / (annualTotals.Count - 1)) - 1) * 100, 1)
            : "";

        // 3) Budget table -> "budget": [ { category, requested, match, justification }, ... ]  (mapping type: table)
        var budget = (await c.QueryAsync("SELECT category, requested, match, justification FROM budget_lines WHERE application_id = @id ORDER BY row_no", new { id = applicationId }))
            .Select(r => new Dictionary<string, object?>((IDictionary<string, object?>)r, StringComparer.OrdinalIgnoreCase)).ToList();
        data["budget"] = budget;
        long req = budget.Sum(r => Convert.ToInt64(r["requested"]));
        long mat = budget.Sum(r => Convert.ToInt64(r["match"]));
        data["total_requested"] = req;
        data["total_match"] = mat;
        data["match_pct"] = req > 0 ? Math.Round(100.0 * mat / req, 1) : "";

        // 4) Funding sources -> "source": [ { name, amount, secured }, ... ]  ('secured' feeds a Yes/No radio)
        data["source"] = (await c.QueryAsync(
            "SELECT name, amount, CASE secured WHEN 1 THEN 'Yes' ELSE 'No' END AS secured FROM funding_sources WHERE application_id = @id ORDER BY row_no", new { id = applicationId }))
            .Select(r => new Dictionary<string, object?>((IDictionary<string, object?>)r, StringComparer.OrdinalIgnoreCase)).ToList();

        // 5) Compliance checkboxes
        var checks = await c.QueryAsync<(string code, long confirmed)>(
            "SELECT code, confirmed FROM compliance_items WHERE application_id = @id", new { id = applicationId });
        foreach (var ck in checks) data[$"chk_{ck.code}"] = ck.confirmed == 1;

        // 6) Signatures
        var sigs = await c.QueryAsync<(string role, string signer_name, string signed_on, byte[] image_png)>(
            "SELECT role, signer_name, signed_on, image_png FROM signatures WHERE application_id = @id", new { id = applicationId });
        foreach (var s in sigs)
        {
            data[$"sig_{s.role}"] = s.image_png;
            data[$"sig_{s.role}_name"] = s.signer_name;
            data[$"sig_{s.role}_date"] = s.signed_on;
        }
        return data;
    }

    private static void Merge(Dictionary<string, object?> target, dynamic row, string prefix, string[]? skip = null)
    {
        foreach (var kv in (IDictionary<string, object?>)row)
            if (skip is null || !skip.Contains(kv.Key))
                target[prefix + kv.Key] = kv.Value;
    }

    private static void Blank(Dictionary<string, object?> target, int rows, string prefixFormat, string[] columns)
    {
        for (var r = 1; r <= rows; r++)
            foreach (var col in columns)
                target[string.Format(prefixFormat, r) + col] = "";
    }
}
