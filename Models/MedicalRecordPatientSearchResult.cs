namespace HBaseMVC.Models
{
    public class MedicalRecordPatientSearchResult
    {
        public Cadaver Patient { get; set; }
            = new Cadaver();

        public List<MedicalRecord> MedicalRecords { get; set; }
            = new List<MedicalRecord>();

        public List<LabTestRecord> LabTestRecords { get; set; }
            = new List<LabTestRecord>();

        public bool AdmissionMatched { get; set; }
            = false;
    }
}