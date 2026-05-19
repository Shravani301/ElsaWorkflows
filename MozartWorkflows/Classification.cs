namespace MozartWorkflows
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Newtonsoft.Json.Linq;

    public static partial class Classification
    {
        // ---------- Weights & thresholds -----------------------------------

        private const double KEYWORD_SCORE = 1.0;
        private const double PATTERN_SCORE = 2.5;

        // If the best label is below this -> LabelUnknown
        private const double MIN_ABSOLUTE_SCORE = 3.0;
        private const string LabelUnknown = "unknown";

        // ---- Internal label keys (appear 3+ times each) --------------------
        private const string LblHospitalBill = "HospitalBill";
        private const string LblDischargeSummary = "DischargeSummary";
        private const string LblMedicalReport = "MedicalReport";
        private const string LblPolicyDocument = "PolicyDocument";
        private const string LblBankStatement = "BankStatement";
        private const string LblInvoice = "Invoice";
        private const string LblAadhaar = "Aadhaar";
        private const string LblPassport = "Passport";

        // ---------- Regexes for KYC / Bank --------------------------------

        // ---------- Public overloads --------------------------------------

        // 1-arg overload (for old call sites)
        public static (string Label, double Confidence, Dictionary<string, double> Extra)
            Classify(string text)
            => Classify(text, null);

        // 2-arg overload (used by your activity)
        #pragma warning disable S3776
        public static (string Label, double Confidence, Dictionary<string, double> Extra)
            Classify(string text, JObject? kvFields)
        {
            // If no text at all – just give up.
            if (string.IsNullOrWhiteSpace(text))
                return (LabelUnknown, 0d, new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));

            var t = text.ToLowerInvariant();
            t = WhitespaceRegex().Replace(t, " "); // collapse whitespace
            var upper = text.ToUpperInvariant();

            var scores = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            void Add(string label, double delta)
            {
                if (Math.Abs(delta) < double.Epsilon) return;
                scores[label] = scores.TryGetValue(label, out var cur) ? cur + delta : delta;
            }

            bool ContainsAny(params string[] keys) =>
                keys.Any(k => t.Contains(k, StringComparison.Ordinal));

            int CountMatches(params string[] keys) =>
                keys.Count(k => t.Contains(k, StringComparison.Ordinal));

            // NOTE: We are **not** using kvFields["DocumentType"] anymore
            // to avoid feedback loops like BankStatement vs PAN.

            // ----------------------------------------------------------------
            // 1. HOSPITAL BILL  (this is your "hospital bill / medical bill" label)
            // ----------------------------------------------------------------
            if (ContainsAny(
                    "ip final bill", "in-patient final bill", "in patient final bill",
                    "final bill", "hospital bill", "ip bill", "ip final bill"))
                Add(LblHospitalBill, 10);

            if (ContainsAny(
                    "detailed final bill", "ip billing", "ip summary bill", "summary bill"))
                Add(LblHospitalBill, 6);

            if (ContainsAny(
                    "hospital charges", "room charges", "bed charges", "icu", "nicu",
                    "ip registration fee", "op registration fee", "op ipd procedure",
                    "procedure charges", "consultation charges", "doctor charges"))
                Add(LblHospitalBill, 6);

            if (ContainsAny(
                    "patient id", "patient name", "ip ref no", "ip no", "uhid",
                    "mr no", "admission date", "date of admission",
                    "discharge date", "date of discharge"))
                Add(LblHospitalBill, 5);

            if (ContainsAny(
                    "pharmacy", "investigations / lab", "investigation charges",
                    "nursing charges", "lab charges", "op ipd procedure"))
                Add(LblHospitalBill, 4);

            if (ContainsAny(
                    "amount in rs.", "amount in rs", "grand total", "total amount",
                    "net payable", "net amount payable", "net payable amount"))
                Add(LblHospitalBill, 4);
            // Explicitly catch OPD / clinic style medical receipts
            if (ContainsAny(
                    "medical bill receipt",
                    "medical bill",
                    "medical receipt"))
                Add(LblHospitalBill, 10);

            // Clinic / doctor receipt style wording
            if (ContainsAny(
                    "name of medical institution",
                    "clinic",
                    "klinik",
                    "practitioner name",
                    "consultation",
                    "medicines",
                    "x-ray fees",
                    "lab / x-ray",
                    "laboratory fees"))
                Add(LblHospitalBill, 4);
            // ----------------------------------------------------------------
            // 2. DISCHARGE SUMMARY
            // ----------------------------------------------------------------
            if (ContainsAny("discharge summary", "summary of discharge"))
                Add(LblDischargeSummary, 10);

            if (ContainsAny(
                    "final diagnosis", "provisional diagnosis",
                    "course in hospital", "course during hospital stay",
                    "condition on discharge", "condition at discharge",
                    "advice on discharge", "treatment given",
                    "treatment during stay", "follow up advice"))
                Add(LblDischargeSummary, 6);

            // ----------------------------------------------------------------
            // 3. MEDICAL REPORT (NOT a bill)
            // ----------------------------------------------------------------
            if (ContainsAny(
                    "laboratory report", "lab report", "investigation report",
                    "pathology report", "radiology report", "biochemistry report",
                    "x-ray report", "x ray report",
                    "mri report", "ct scan report", "ultrasound report"))
                Add(LblMedicalReport, 8);

            if (ContainsAny(
                    "test name", "parameter", "result value", "result",
                    "reference range", "normal range", "units"))
                Add(LblMedicalReport, 5);

            if (ContainsAny(
                    "hemoglobin", "wbc", "rbc", "platelet", "bilirubin",
                    "serum", "creatinine", "glucose", "cholesterol",
                    "sgot", "sgpt", "hdl", "ldl"))
                Add(LblMedicalReport, 3);

            // If clearly a bill, penalise medical-report label.
            if (t.Contains("final bill") || t.Contains("ip billing") || t.Contains("bill no"))
                Add(LblMedicalReport, -8);

            // ----------------------------------------------------------------
            // 4. POLICY / INSURANCE DOCUMENT
            // ----------------------------------------------------------------
            if (ContainsAny(
                    "policy document", "policy schedule", "certificate of insurance",
                    "insurance policy", "health insurance", "life insurance"))
                Add(LblPolicyDocument, 8);

            if (ContainsAny(
                    "policy number", "policy no", "policy #",
                    "sum assured", "sum insured", "insured person", "life assured",
                    "premium", "due date", "maturity date",
                    "date of commencement", "date of inception"))
                Add(LblPolicyDocument, 5);

            if (ContainsAny(
                    "health card", "policy card", "tpa card", "e-card", "ecard"))
                Add(LblPolicyDocument, 3);

            // ----------------------------------------------------------------
            // 5. BANK STATEMENT
            // ----------------------------------------------------------------
            if (ContainsAny(
                    "bank statement", "statement of account", "account statement",
                    "passbook"))
                Add(LblBankStatement, 8);

            if (ContainsAny(
                    "account number", "account no", "a/c no", "ac no", "acc no",
                    "account #", "ifsc", "ifsc code", "micr", "branch",
                    "transaction date", "txn date", "value date",
                    "cheque no", "chq no",
                    "opening balance", "closing balance", "available balance"))
                Add(LblBankStatement, 5);

            if (ContainsAny("neft", "rtgs", "imps", "upi", "narration", "description",
                            "credit", "debit"))
                Add(LblBankStatement, 3);

            if (IfscRegex().IsMatch(text))
                Add(LblBankStatement, 3);

            // ----------------------------------------------------------------
            // 6. INVOICE / UTILITY BILL / CHEQUE (generic)
            // ----------------------------------------------------------------
            if (ContainsAny("invoice", "tax invoice", "tax-invoice", "invoice no", "invoice #", "bill to"))
                Add(LblInvoice, 8);

            if (ContainsAny("gstin", "hsn", "sac", "quantity", "rate", "amount"))
                Add(LblInvoice, 4);

            if (ContainsAny("electricity bill", "water bill", "gas bill",
                            "consumer number", "service number", "meter no"))
                Add("UtilityBill", 6);

            if (ContainsAny("cheque", "check no", "pay against this cheque",
                            "crossed cheque", "payee", "drawer"))
                Add("Cheque", 5);

            // ----------------------------------------------------------------
            // 7. UNDERWRITING / SALARY / FORM16
            // ----------------------------------------------------------------
            if (ContainsAny(
                    "underwriting", "medical underwriting", "financial underwriting",
                    "risk assessment", "underwriting remarks"))
                Add("UnderwritingDocument", 8);

            if (ContainsAny(
                    "proposal form", "proposal number", "proposal no",
                    "sum at risk", "income proof", "itr", "income tax return",
                    "form 16", "salary slip", "payslip", "pay slip"))
            {


                if (ContainsAny("salary slip", "payslip", "pay slip"))
                    Add("SalarySlip", 6);

                if (ContainsAny("form 16"))
                    Add("Form16", 6);
            }

            // ----------------------------------------------------------------
            // 8. KYC DOCUMENTS – Aadhaar / PAN / Voter / Passport / Driving Licence
            //     *** NO special override: just normal scoring ***
            // ----------------------------------------------------------------

            // Aadhaar
            int aadhaarWordHits = CountMatches(
                "aadhaar", "aadhar", "uidai", "unique identification authority of india");
            bool hasAadhaarNumber = AadhaarNumberRegex().IsMatch(upper);

            // Aadhaar: strong when word + number; weaker when only one of them.
            if (aadhaarWordHits >= 1 && hasAadhaarNumber)
                Add(LblAadhaar, 10 + aadhaarWordHits * KEYWORD_SCORE + PATTERN_SCORE);
            else if (aadhaarWordHits >= 2 || hasAadhaarNumber)
                Add(LblAadhaar, 6 + aadhaarWordHits * KEYWORD_SCORE);

            // PAN
            int panWordHits = CountMatches(
                "permanent account number", "permanent account no",
                "pan card", "pan no",
                "income tax department", "income-tax department",
                "govt. of india", "government of india");
            bool hasPanNumber = PanNumberRegex().IsMatch(upper);

            // PAN card sample: “income tax department” + “permanent account number card”
            // → panWordHits >= 2 even if number is missed.
            if (panWordHits >= 2 && hasPanNumber)
                Add("Pan", 10 + panWordHits * KEYWORD_SCORE + PATTERN_SCORE);
            else if (panWordHits >= 2 || (panWordHits >= 1 && hasPanNumber))
                Add("Pan", 7 + panWordHits * KEYWORD_SCORE);

            // Voter ID
            int voterWordHits = CountMatches(
                "election commission of india", "elector photo identity card",
                "epic no", "voter id", "voter identity card", "epic no.");
            bool hasVoterNumber = VoterIdRegex().IsMatch(upper);

            if (voterWordHits >= 1 && hasVoterNumber)
                Add("VoterId", 10 + voterWordHits * KEYWORD_SCORE + PATTERN_SCORE);
            else if (voterWordHits >= 2)
                Add("VoterId", 7 + voterWordHits * KEYWORD_SCORE);

            // Passport
            int passportWordHits = CountMatches(
                "passport", "passport no", "passport number", "republic of india");
            bool hasPassportNumber = PassportNumberRegex().IsMatch(upper);

            if (passportWordHits >= 1 && hasPassportNumber)
                Add(LblPassport, 10 + passportWordHits * KEYWORD_SCORE + PATTERN_SCORE);
            else if (passportWordHits >= 2)
                Add(LblPassport, 7 + passportWordHits * KEYWORD_SCORE);

            // Driving Licence
            int dlWordHits = CountMatches(
                "driving licence", "driving license",
                "driver licence", "driver license",
                "dl no", "dl number",
                "licence no", "license no");
            bool hasDlNumber = DrivingLicenceNumberRegex().IsMatch(upper);

            if (dlWordHits >= 1 && hasDlNumber)
                Add("DrivingLicence", 10 + dlWordHits * KEYWORD_SCORE + PATTERN_SCORE);
            else if (dlWordHits >= 2)
                Add("DrivingLicence", 7 + dlWordHits * KEYWORD_SCORE);


            // ----------------------------------------------------------------
            // POST-PROCESSING
            // ----------------------------------------------------------------

            // Remove <= 0 scores (penalised, etc.).
            foreach (var key in scores.Keys.ToList())
            {
                if (scores[key] <= 0)
                    scores.Remove(key);
            }

            // Nothing survived → unknown.
            if (scores.Count == 0)
                return (LabelUnknown, 0d, new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));

            double bestScore = scores.Values.Max();
            double sum = scores.Values.Sum();

            // Best signal still too weak → unknown.
            if (bestScore < MIN_ABSOLUTE_SCORE)
                return (LabelUnknown, 0d, scores);



            // Normalise for debug
            var normScores = scores.ToDictionary(
                kv => kv.Key,
                kv => Math.Round(kv.Value / sum, 3),
                StringComparer.OrdinalIgnoreCase);

            // Pick the best label
            var best = normScores.OrderByDescending(kv => kv.Value).First();

            // Map internal PascalCase label → nice display label
            string displayLabel = MapDisplayLabel(best.Key);

            // best.Value is the relative share (0–1), good as "confidence"
            return (displayLabel, best.Value, normScores);

        }
#pragma warning restore S3776

        // Turn internal keys into user-friendly labels
        private static string MapDisplayLabel(string internalKey) =>
            internalKey switch
            {
                LblHospitalBill => "Hospital Bill",
                LblDischargeSummary => "Discharge Summary",
                LblMedicalReport => "Medical Report",
                LblPolicyDocument => "Policy Document",
                LblBankStatement => "Bank Statement",
                "SalarySlip" => "Salary Slip",
                "Form16" => "Form 16",
                LblInvoice => "Invoice",
                "UtilityBill" => "Utility Bill",
                "Cheque" => "Cheque",
                LblAadhaar => "Aadhaar",
                "Pan" => "PAN",
                "VoterId" => "Voter Id",
                LblPassport => "Passport",
                "DrivingLicence" => "Driving Licence",
                _ => internalKey
            };

        [GeneratedRegex(@"\b\d{4}\s?\d{4}\s?\d{4}\b", RegexOptions.None)]
        private static partial Regex AadhaarNumberRegex();

        [GeneratedRegex(@"\b[A-Z]{5}[0-9]{4}[A-Z]\b", RegexOptions.IgnoreCase)]
        private static partial Regex PanNumberRegex();

        [GeneratedRegex(@"\b[A-Z]{3}[0-9]{7}\b", RegexOptions.IgnoreCase)]
        private static partial Regex VoterIdRegex();

        [GeneratedRegex(@"\b[A-Z]{4}0[A-Z0-9]{6}\b", RegexOptions.IgnoreCase)]
        private static partial Regex IfscRegex();

        [GeneratedRegex(@"\b[A-Z][0-9]{7}\b", RegexOptions.IgnoreCase)]
        private static partial Regex PassportNumberRegex();

        [GeneratedRegex(@"\b[A-Z]{2}\d{2}\s?\d{6,11}\b", RegexOptions.IgnoreCase)]
        private static partial Regex DrivingLicenceNumberRegex();

        [GeneratedRegex(@"\s+", RegexOptions.None)]
        private static partial Regex WhitespaceRegex();
    }
}
