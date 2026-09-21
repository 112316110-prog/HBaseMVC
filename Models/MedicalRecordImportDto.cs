namespace HBaseMVC.Models
{
    public class MedicalRecordImportDto
    {
        public string Row { get; set; } = "";

        // patient:*
        public string Gender { get; set; } = "";
        public string Age { get; set; } = "";
        public string BloodType { get; set; } = "";
        public string DonationDate { get; set; } = "";
        public string DissectionDate { get; set; } = "";
        public string NhisPatientId { get; set; } = "";
        public string BirthYearMonth { get; set; } = "";
        public string DataSource { get; set; } = "import";
        public string DataVersion { get; set; } = "";

        // teaching:*
        public string DissectionProcessRecord { get; set; } = "";
        public string TeacherExplanationRecord { get; set; } = "";
        public string TeachingNote { get; set; } = "";
        public string PathologicalFindings { get; set; } = "";
        public string RelatedDiagnosisCode { get; set; } = "";
        public string RelatedTestItemCode { get; set; } = "";
        public string RelatedOrderCode { get; set; } = "";

        // lab_test:*
        public string LabTestDate { get; set; } = "";
        public string LabTestItem { get; set; } = "";
        public string LabTestResult { get; set; } = "";
        public string LabTestItemCode { get; set; } = "";
        public string LabTestItemName { get; set; } = "";
        public string LabResultType { get; set; } = "";
        public string LabNumericResult { get; set; } = "";
        public string LabTextResult { get; set; } = "";
        public string LabImageResultPath { get; set; } = "";
        public string LabWaveformResultPath { get; set; } = "";
        public string LabAudioResultPath { get; set; } = "";
        public string LabUnit { get; set; } = "";
        public string LabReferenceRange { get; set; } = "";
        public string LabAbnormalFlag { get; set; } = "";
        public string LabTestMethod { get; set; } = "";
        public string LabSpecimenCollectedAt { get; set; } = "";

        // admission:*
        public string AdmissionStartDate { get; set; } = "";
        public string AdmissionEndDate { get; set; } = "";
        public string WardType { get; set; } = "";
        public string AdmissionReason { get; set; } = "";
        public string AdmissionReasonCode { get; set; } = "";
        public string AdmissionDiagnosisCode { get; set; } = "";
        public string DischargeDiagnosisCode { get; set; } = "";
        public string PrimaryDiagnosisCode { get; set; } = "";
        public string SecondaryDiagnosisCode { get; set; } = "";
        public string HospitalCode { get; set; } = "";
        public string DepartmentCode { get; set; } = "";
        public string DischargeStatus { get; set; } = "";
        public string LengthOfStay { get; set; } = "";
        public string IcuDays { get; set; } = "";
        public string InpatientOrderCode { get; set; } = "";
        public string ProcedureCode { get; set; } = "";
    }
}