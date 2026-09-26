namespace HBaseMVC.Models
{
    public class Cadaver
    {
        // =========================
        // RowKey：HBase 每一列的主鍵
        // =========================
        public string RowKey { get; set; } = "";
        // patient:id
        public string PatientId { get; set; } = "";

        // patient:medical_record_number
        public string MedicalRecordNumber { get; set; } = "";

        // patient:gender
        public string Gender { get; set; } = "";

        // patient:age
        public string Age { get; set; } = "";

        // patient:blood_type
        public string BloodType { get; set; } = "";

        // patient:blood_type_source
        public string BloodTypeSource { get; set; } = "";

        // patient:drug_allergy_history
        public string DrugAllergyHistory { get; set; } = "";

        // patient:past_medical_history
        public string PastMedicalHistory { get; set; } = "";

        // 以下舊欄位先保留，稍後移到病歷頁填寫
        public string DonationDate { get; set; } = "";
        public string DissectionDate { get; set; } = "";
        public string NhisPatientId { get; set; } = "";
        public string BirthYearMonth { get; set; } = "";
        public string DataSource { get; set; } = "";
        public string DataVersion { get; set; } = "";
        // 舊欄位相容用
        // 原本程式使用 info:donation_year
        // 先保留，避免 Index.cshtml 或 Controller 舊程式報錯
        public string DonationYear { get; set; } = "";

        // =========================
        // teaching:* 大體解剖上課資料
        // 對應 HBase teaching column family
        // =========================

        // teaching:dissection_process_record
        public string DissectionProcessRecord { get; set; } = "";

        // teaching:teacher_explanation_record
        public string TeacherExplanationRecord { get; set; } = "";

        // teaching:teaching_note
        public string TeachingNote { get; set; } = "";

        // teaching:pathological_findings
        public string PathologicalFindings { get; set; } = "";

        // teaching:related_diagnosis_code
        public string RelatedDiagnosisCode { get; set; } = "";

        // teaching:related_test_item_code
        public string RelatedTestItemCode { get; set; } = "";

        // teaching:related_order_code
        public string RelatedOrderCode { get; set; } = "";

        // info:image_*
        public List<string> ImagePaths { get; set; } = new();

        // image 的 id，通常來自 info:image_{id}
        public List<string> ImageIds { get; set; } = new();

        // info:drawing_*
        public List<string> DrawingJsonList { get; set; } = new();

        // info:video_*
        public List<string> VideoPaths { get; set; } = new();

        public List<string> VideoTypes { get; set; } = new List<string>();

        // info:note_*
        public List<string> Notes { get; set; } = new();

        // info:isAlive_*
        public List<bool> IsAliveImages { get; set; } = new();

        // 影片說明
        public List<string> VideoNotes { get; set; } = new();

        // info:category_*
        public List<string> VideoCategories { get; set; } = new();

        // 影片是否為生前影像
        public List<bool> VideoIsAliveImages { get; set; } = new();

        // info:fhir_patient_json 或 fhir:patient_json
        public string PatientFhirJson { get; set; } = "";

        // info:fhir_patient_updated_at 或 fhir:patient_updated_at
        public string PatientLastUpdated { get; set; } = "";

        public List<string> LifePhotoIds { get; set; }
            = new List<string>();

        public List<string> LifePhotoPaths { get; set; }
            = new List<string>();

        public List<string> LifePhotoNotes { get; set; }
            = new List<string>();

        // lab_test:test_date
        public string LabTestDate { get; set; } = "";

        // lab_test:test_item
        public string LabTestItem { get; set; } = "";

        // lab_test:test_result
        public string LabTestResult { get; set; } = "";

        // lab_test:test_item_code
        public string LabTestItemCode { get; set; } = "";

        // lab_test:test_item_name
        public string LabTestItemName { get; set; } = "";

        // lab_test:result_type
        public string LabResultType { get; set; } = "";

        // lab_test:numeric_result
        public string LabNumericResult { get; set; } = "";

        // lab_test:text_result
        public string LabTextResult { get; set; } = "";

        // lab_test:image_result_path
        public string LabImageResultPath { get; set; } = "";

        // lab_test:waveform_result_path
        public string LabWaveformResultPath { get; set; } = "";

        // lab_test:audio_result_path
        public string LabAudioResultPath { get; set; } = "";

        // lab_test:unit
        public string LabUnit { get; set; } = "";

        // lab_test:reference_range
        public string LabReferenceRange { get; set; } = "";

        // lab_test:abnormal_flag
        public string LabAbnormalFlag { get; set; } = "";

        // lab_test:test_method
        public string LabTestMethod { get; set; } = "";

        // lab_test:specimen_collected_at
        public string LabSpecimenCollectedAt { get; set; } = "";

        // admission:start_date
        public string AdmissionStartDate { get; set; } = "";

        // admission:end_date
        public string AdmissionEndDate { get; set; } = "";

        // admission:ward_type
        public string WardType { get; set; } = "";

        // admission:admission_reason
        public string AdmissionReason { get; set; } = "";

        // admission:admission_reason_code
        public string AdmissionReasonCode { get; set; } = "";

        // admission:admission_diagnosis_code
        public string AdmissionDiagnosisCode { get; set; } = "";

        // admission:discharge_diagnosis_code
        public string DischargeDiagnosisCode { get; set; } = "";

        // admission:primary_diagnosis_code
        public string PrimaryDiagnosisCode { get; set; } = "";

        // admission:secondary_diagnosis_code
        public string SecondaryDiagnosisCode { get; set; } = "";

        // admission:hospital_code
        public string HospitalCode { get; set; } = "";

        // admission:department_code
        public string DepartmentCode { get; set; } = "";

        // admission:discharge_status
        public string DischargeStatus { get; set; } = "";

        // admission:length_of_stay
        public string LengthOfStay { get; set; } = "";

        // admission:icu_days
        public string IcuDays { get; set; } = "";

        // admission:inpatient_order_code
        public string InpatientOrderCode { get; set; } = "";

        // admission:procedure_code
        public string ProcedureCode { get; set; } = "";
        // 病歷清單
        public List<MedicalRecord> MedicalRecords { get; set; }
            = new List<MedicalRecord>();
        public bool IsDissected { get; set; } = false;

        // 多筆檢驗紀錄
        public List<LabTestRecord> LabTestRecords { get; set; }
            = new List<LabTestRecord>();

        // =========================
        // 課程 / 解剖影像
        // course_image:*
        // =========================

        public List<string> CourseImagePaths { get; set; }
            = new();

        public List<string> CourseImageIds { get; set; }
            = new();

        public List<string> CourseImageNotes { get; set; }
            = new();

        public List<string> CourseDrawingJsonList { get; set; }
            = new();

        // course_image:anno_img_*
        public List<string> CourseAnnotatedImagePaths { get; set; }
            = new();


        // =========================
        // 病歷 / 病理影像
        // medical_image:*
        // =========================

        public List<string> MedicalImagePaths { get; set; }
            = new();

        public List<string> MedicalImageIds { get; set; }
            = new();

        public List<string> MedicalImageNotes { get; set; }
            = new();
        public List<string> MedicalImageExaminationTypes { get; set; }
        = new List<string>();
        public List<string> MedicalDrawingJsonList { get; set; }
            = new();

        public List<string> MedicalAnnotatedImagePaths { get; set; }
            = new();

    }
}