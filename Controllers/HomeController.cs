using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HBaseMVC.Models;
using HBaseMVC.Services;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace HBaseMVC.Controllers
{
    public sealed class EnsurePaperPatientRequest
    {
        public string Row { get; set; } = "";

        public string MedicalRecordNumber { get; set; } = "";
    }
    public sealed class BodyMarkPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class BodyAnnotationMark
    {
        public string Type { get; set; } = "";

        public double X { get; set; }
        public double Y { get; set; }

        public List<BodyMarkPoint> Points { get; set; } = new();
    }

    public sealed class SaveBodyAnnotationRequest
    {
        public string Row { get; set; } = "";

        public List<BodyAnnotationMark> Marks { get; set; } = new();
    }

    public sealed class QuizQuestion
    {
        public string Id { get; set; } = "";
        public string QuestionText { get; set; } = "";
        public string OptionA { get; set; } = "";
        public string OptionB { get; set; } = "";
        public string OptionC { get; set; } = "";
        public string OptionD { get; set; } = "";
        public string CorrectAnswer { get; set; } = "";
        public string Explanation { get; set; } = "";
        public bool IsActive { get; set; } = true;
        public string CreatedBy { get; set; } = "";
        public string CreatedAt { get; set; } = "";
    }


    public sealed class QuizQuestionSnapshot
    {
        public string Id { get; set; } = "";
        public string QuestionText { get; set; } = "";
        public string OptionA { get; set; } = "";
        public string OptionB { get; set; } = "";
        public string OptionC { get; set; } = "";
        public string OptionD { get; set; } = "";
        public string CorrectAnswer { get; set; } = "";
        public string Explanation { get; set; } = "";
    }

    public sealed class QuizAttemptRecord
    {
        public string AttemptId { get; set; } = "";
        public string StudentId { get; set; } = "";
        public int Score { get; set; }
        public int Total { get; set; }
        public Dictionary<string, string> Answers { get; set; } = new();
        public List<QuizQuestionSnapshot> Questions { get; set; } = new();
        public string QuizVersion { get; set; } = "";
        public string SubmittedAt { get; set; } = "";
    }

    public sealed class QuizStudentProgress
    {
        public string StudentId { get; set; } = "";
        public bool HasAnswered { get; set; }
        public int Score { get; set; }
        public int Total { get; set; }
        public string SubmittedAt { get; set; } = "";
        public string AttemptId { get; set; } = "";
    }
    public sealed class QuizVersionArchive
    {
        public string QuizVersion { get; set; } = "";

        public List<QuizQuestionSnapshot> Questions { get; set; }
            = new();

        public int AttemptCount { get; set; }

        public string FirstSubmittedAt { get; set; } = "";

        public string LastSubmittedAt { get; set; } = "";
    }
    public sealed class QuizQuestionVersion
    {
        public string VersionName { get; set; } = "";

        public List<QuizQuestionSnapshot> Questions { get; set; }
            = new();

        public bool IsCurrent { get; set; }

        public bool IsTest { get; set; }
    }
    public sealed class QuizProgressVersion
    {
        public string QuizVersion { get; set; } = "";

        public string DisplayName { get; set; } = "";

        public List<QuizQuestionSnapshot> Questions { get; set; }
            = new();

        public bool IsCurrent { get; set; }

        public bool IsTest { get; set; }
    }
    public class HomeController : Controller
    {
        private readonly HttpClient _client;
        private readonly HttpClient _ollamaClient;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
    IHttpClientFactory httpClientFactory,
    ILogger<HomeController> logger)
        {
            // HBase REST API
            _client = new HttpClient
            {
                BaseAddress = new Uri("http://localhost:8080/"),
                Timeout = TimeSpan.FromSeconds(120)
            };

            // Ollama API，由 Program.cs 的 AddHttpClient("Ollama") 建立
            _ollamaClient =
                httpClientFactory.CreateClient("Ollama");

            _logger = logger;
        }

        private string ToBase64(string text) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(text ?? ""));

        private string Decode(string base64) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        private static byte[]? ExtractHBaseCellBytes(
            string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return null;
            }

            XDocument document =
                XDocument.Parse(xml);

            XElement? cell =
                document
                    .Descendants()
                    .FirstOrDefault(x =>
                        x.Name.LocalName == "Cell"
                    );

            if (cell == null)
            {
                return null;
            }

            string base64 =
                cell.Value?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(base64))
            {
                return null;
            }

            return Convert.FromBase64String(
                base64
            );
        }
        // ============================================================
        // AI Intent Router
        // 先判斷使用者真正想查什麼，再由 C# 精確查 HBase。
        // Ollama 只負責將已查到的資料整理成人話。
        // ============================================================
        private sealed class AiIntentResult
        {
            public string Intent { get; set; } = "unknown";
            public string PatientId { get; set; } = "";
            public string Gender { get; set; } = "";
            public int? MinAge { get; set; }
            public int? MaxAge { get; set; }
            public string Keyword { get; set; } = "";

            public string ExaminationType { get; set; } = "";
        }

        private static bool ContainsAny(
            string text,
            params string[] values)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return values.Any(value =>
                text.Contains(
                    value,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        }

        private static bool IsSamePatientIdentifier(
            Cadaver patient,
            string identifier)
        {
            if (patient == null ||
                string.IsNullOrWhiteSpace(identifier))
            {
                return false;
            }

            identifier = identifier.Trim();

            return
                string.Equals(
                    patient.RowKey?.Trim(),
                    identifier,
                    StringComparison.OrdinalIgnoreCase
                )
                ||
                string.Equals(
                    patient.PatientId?.Trim(),
                    identifier,
                    StringComparison.OrdinalIgnoreCase
                )
                ||
                string.Equals(
                    patient.MedicalRecordNumber?.Trim(),
                    identifier,
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private string ExtractExactPatientId(
            string question,
            List<Cadaver> patients)
        {
            if (string.IsNullOrWhiteSpace(question) ||
                patients == null ||
                patients.Count == 0)
            {
                return "";
            }

            // 由長到短比對，避免 P002 先被 2 命中。
            var identifiers =
                patients
                    .SelectMany(patient =>
                        new[]
                        {
                            patient.RowKey,
                            patient.PatientId,
                            patient.MedicalRecordNumber
                        }
                    )
                    .Where(value =>
                        !string.IsNullOrWhiteSpace(value)
                    )
                    .Select(value =>
                        value!.Trim()
                    )
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase
                    )
                    .OrderByDescending(value =>
                        value.Length
                    )
                    .ToList();

            foreach (string identifier in identifiers)
            {
                string pattern =
                    $@"(?<![A-Za-z0-9_-]){Regex.Escape(identifier)}(?![A-Za-z0-9_-])";

                if (Regex.IsMatch(
                        question,
                        pattern,
                        RegexOptions.IgnoreCase))
                {
                    return identifier;
                }
            }

            return "";
        }

        private string ExtractExplicitPatientToken(
            string question)
        {
            if (string.IsNullOrWhiteSpace(question))
                return "";

            // 例如 P002、DEMO051、C006。
            Match alphaNumeric =
                Regex.Match(
                    question,
                    @"(?<![A-Za-z0-9_-])([A-Za-z][A-Za-z0-9_-]*\d+[A-Za-z0-9_-]*)(?![A-Za-z0-9_-])",
                    RegexOptions.IgnoreCase
                );

            if (alphaNumeric.Success)
                return alphaNumeric.Groups[1].Value.Trim();

            // 若句子明確在說「病人 / 病患 / 編號」，才把純數字視為病人 ID。
            if (ContainsAny(
                    question,
                    "病人",
                    "病患",
                    "患者",
                    "編號",
                    "病歷號"))
            {
                Match numeric =
                    Regex.Match(
                        question,
                        @"(?<![A-Za-z0-9_-])(\d+)(?![A-Za-z0-9_-])"
                    );

                if (numeric.Success)
                    return numeric.Groups[1].Value.Trim();
            }

            return "";
        }

        private string ResolvePreviousPatientId(
            string latestQuestion,
            List<Cadaver> patients,
            List<(string Role, string Content)> messages)
        {
            string direct =
                ExtractExactPatientId(
                    latestQuestion,
                    patients
                );

            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            string explicitToken =
                ExtractExplicitPatientToken(
                    latestQuestion
                );

            if (!string.IsNullOrWhiteSpace(explicitToken))
                return explicitToken;

            bool isFollowUp =
                ContainsAny(
                    latestQuestion,
                    "他",
                    "他的",
                    "那他",
                    "那他的",
                    "該病人",
                    "這個病人",
                    "這位病人",
                    "那位病人",
                    "那有",
                    "呢"
                );

            if (!isFollowUp)
                return "";

            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(
                        messages[i].Role,
                        "user",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string previous =
                    messages[i].Content;

                if (string.Equals(
                        previous,
                        latestQuestion,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                string patientId =
                    ExtractExactPatientId(
                        previous,
                        patients
                    );

                if (!string.IsNullOrWhiteSpace(patientId))
                    return patientId;

                patientId =
                    ExtractExplicitPatientToken(
                        previous
                    );

                if (!string.IsNullOrWhiteSpace(patientId))
                    return patientId;
            }

            return "";
        }

        private string BuildContextualQuestion(
            string latestQuestion,
            string patientId)
        {
            if (string.IsNullOrWhiteSpace(patientId))
                return latestQuestion;

            string alreadyHasId =
                ExtractExplicitPatientToken(
                    latestQuestion
                );

            if (!string.IsNullOrWhiteSpace(alreadyHasId))
                return latestQuestion;

            return
                $"病人編號 {patientId}；{latestQuestion}";
        }
        private string ExtractExaminationType(
    string question)
        {
            if (string.IsNullOrWhiteSpace(question))
                return "";

            if (ContainsAny(
                    question,
                    "X光",
                    "X 光",
                    "X光片",
                    "X 光片",
                    "XRay",
                    "X-Ray"))
            {
                return "xray";
            }

            if (ContainsAny(
                    question,
                    "CT",
                    "電腦斷層",
                    "電腦斷層掃描",
                    "Computed Tomography"))
            {
                return "ct";
            }

            if (ContainsAny(
                    question,
                    "MRI",
                    "磁振造影",
                    "核磁共振",
                    "磁共振"))
            {
                return "mri";
            }

            if (ContainsAny(
                    question,
                    "超音波",
                    "超聲波",
                    "Ultrasound"))
            {
                return "ultrasound";
            }

            if (ContainsAny(
                    question,
                    "內視鏡",
                    "Endoscopy"))
            {
                return "endoscopy";
            }

            if (ContainsAny(
                    question,
                    "病理切片",
                    "組織切片",
                    "病理影像",
                    "Pathology",
                    "Histology"))
            {
                return "pathology";
            }

            if (ContainsAny(
                    question,
                    "顯微鏡",
                    "顯微影像",
                    "Microscopy"))
            {
                return "microscopy";
            }

            if (ContainsAny(
                    question,
                    "PET",
                    "正子攝影",
                    "正子斷層"))
            {
                return "pet";
            }

            if (ContainsAny(
                    question,
                    "乳房攝影",
                    "Mammography"))
            {
                return "mammography";
            }

            if (ContainsAny(
                    question,
                    "血管攝影",
                    "Angiography"))
            {
                return "angiography";
            }

            return "";
        }
        private AiIntentResult ParseAiIntent(
            string question,
            List<Cadaver> patients)
        {
            var result =
                new AiIntentResult();

            string q =
                question?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(q))
                return result;

            string exactPatientId =
                ExtractExactPatientId(
                    q,
                    patients
                );

            result.PatientId =
                !string.IsNullOrWhiteSpace(
                    exactPatientId
                )
                    ? exactPatientId
                    : ExtractExplicitPatientToken(q);

            string examinationType =
                ExtractExaminationType(q);

            result.ExaminationType =
                examinationType;

            bool asksSpecificExamination =
                !string.IsNullOrWhiteSpace(
                    examinationType
                );

            bool asksVideo =
                ContainsAny(
                    q,
                    "影片",
                    "視頻",
                    "video"
                );

            bool asksImage =
                ContainsAny(
                    q,
                    "影像",
                    "圖片",
                    "照片",
                    "影像結果"
                );

            bool asksList =
                ContainsAny(
                    q,
                    "哪些",
                    "哪一些",
                    "有哪些",
                    "列出",
                    "全部",
                    "所有",
                    "誰有",
                    "哪些人",
                    "哪些病人",
                    "哪些病患",

                    // 疾病 / 病歷搜尋常用語
                    "相關病歷",
                    "相關病例",
                    "相關病人",
                    "相關病患",
                    "病例",
                    "病歷",

                    // 口語化跨病人搜尋
                    "幫我找",
                    "帮我找",
                    "找一下",
                    "查一下",
                    "我想看",
                    "想看",
                    "有沒有人的",
                    "誰的"
                );

            bool asksLab =
                ContainsAny(
                    q,
                    "檢驗",
                    "檢查",
                    "數值",
                    "參考範圍",
                    "異常標記",
                    "檢驗值",
                    "檢驗結果",
                    "檢驗項目",
                    "異常",
                    "正常",
                    "不正常",
                    "有問題",
                    "偏高",
                    "偏低",
                    "過高",
                    "過低",
                    "太高",
                    "太低",
                    "超標",
                    "CBC",
                    "血液",
                    "生化"
                );

            if (asksImage &&
                asksList &&
                string.IsNullOrWhiteSpace(
                    result.PatientId))
            {
                result.Intent =
                    "list_patients_with_images";

                return result;
            }

            if (asksVideo &&
                !string.IsNullOrWhiteSpace(
                    result.PatientId))
            {
                result.Intent =
                    "patient_video_query";

                return result;
            }

            if (asksSpecificExamination &&
                !string.IsNullOrWhiteSpace(
                    result.PatientId))
            {
                result.Intent =
                    "patient_examination_image_query";

                return result;
            }

            if (asksSpecificExamination &&
                string.IsNullOrWhiteSpace(
                      result.PatientId))
            {
                result.Intent =
                    "examination_image_query";

                return result;
            }

            if (asksImage &&
                !string.IsNullOrWhiteSpace(
                    result.PatientId))
            {
                result.Intent =
                    "patient_image_query";

                return result;
            }

            Match minAgeMatch =
                Regex.Match(
                    q,
                    @"(\d{1,3})\s*歲\s*(?:以上|以內?上|以上者)"
                );

            if (minAgeMatch.Success &&
                int.TryParse(
                    minAgeMatch.Groups[1].Value,
                    out int minAge))
            {
                result.MinAge = minAge;
            }

            Match maxAgeMatch =
                Regex.Match(
                    q,
                    @"(\d{1,3})\s*歲\s*(?:以下|以內|以下者)"
                );

            if (maxAgeMatch.Success &&
                int.TryParse(
                    maxAgeMatch.Groups[1].Value,
                    out int maxAge))
            {
                result.MaxAge = maxAge;
            }

            if (ContainsAny(
                    q,
                    "男性",
                    "男生",
                    "男病人",
                    "男病患"))
            {
                result.Gender = "男";
            }
            else if (ContainsAny(
                    q,
                    "女性",
                    "女生",
                    "女病人",
                    "女病患"))
            {
                result.Gender = "女";
            }

            bool hasFilter =
                result.MinAge.HasValue
                ||
                result.MaxAge.HasValue
                ||
                !string.IsNullOrWhiteSpace(
                    result.Gender
                );

            if (hasFilter &&
                asksList)
            {
                result.Intent =
                    "patient_filter";

                return result;
            }

            if (asksLab)
            {
                result.Intent =
                    "lab_query";

                return result;
            }

            if (!string.IsNullOrWhiteSpace(
                    result.PatientId))
            {
                result.Intent =
                    "patient_detail";

                return result;
            }

            // 沒有指定病人，但有「相關病歷 / 哪些病人 / 病例」等查詢，
            // 視為跨病人條件搜尋。
            if (asksList)
            {
                result.Intent =
                    "patient_filter";

                List<string> searchKeywords =
                    ExtractSearchKeywords(q);

                result.Keyword =
                    searchKeywords
                        .FirstOrDefault(x =>
                            x.Length >= 2
                            &&
                            !int.TryParse(x, out _)
                        )
                    ?? "";

                return result;
            }

            // ------------------------------------------------------------
            // 裸關鍵字 / 疾病名稱搜尋
            // 例如：糖尿病、肺癌、高血壓、肝癌。
            // 不維護疾病白名單；只要目前 HBase 病歷內容確實有命中，
            // 就把它視為跨病人條件搜尋。
            // ------------------------------------------------------------
            List<string> fallbackKeywords =
                ExtractSearchKeywords(q);

            bool matchesExistingMedicalRecord =
                fallbackKeywords.Count > 0
                && patients.Any(patient =>
                    CalculatePatientMatchScore(
                        patient,
                        q,
                        fallbackKeywords
                    ) > 0
                );

            if (matchesExistingMedicalRecord)
            {
                result.Intent =
                    "patient_filter";

                result.Keyword =
                    fallbackKeywords
                        .FirstOrDefault(x =>
                            x.Length >= 2
                            &&
                            !int.TryParse(x, out _)
                        )
                    ?? q;

                return result;
            }

            result.Intent =
                "unknown";

            return result;
        }

        private Cadaver? FindPatientByIdentifier(
            string identifier,
            List<Cadaver> patients)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return null;

            return patients.FirstOrDefault(
                patient =>
                    IsSamePatientIdentifier(
                        patient,
                        identifier
                    )
            );
        }

        private List<Cadaver> FilterPatientsByIntent(
            AiIntentResult intent,
            string question,
            List<Cadaver> patients)
        {
            IEnumerable<Cadaver> query =
                patients;

            if (intent.MinAge.HasValue)
            {
                query =
                    query.Where(patient =>
                        int.TryParse(
                            patient.Age,
                            out int age)
                        &&
                        age >=
                            intent.MinAge.Value
                    );
            }

            if (intent.MaxAge.HasValue)
            {
                query =
                    query.Where(patient =>
                        int.TryParse(
                            patient.Age,
                            out int age)
                        &&
                        age <=
                            intent.MaxAge.Value
                    );
            }

            if (!string.IsNullOrWhiteSpace(
                    intent.Gender))
            {
                query =
                    query.Where(patient =>
                        string.Equals(
                            patient.Gender?.Trim(),
                            intent.Gender,
                            StringComparison.OrdinalIgnoreCase
                        )
                        ||
                        (
                            intent.Gender == "男"
                            &&
                            string.Equals(
                                patient.Gender?.Trim(),
                                "male",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        ||
                        (
                            intent.Gender == "女"
                            &&
                            string.Equals(
                                patient.Gender?.Trim(),
                                "female",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    );
            }

            // 如果只有年齡／性別條件，就不要再做文字相似度，
            // 避免把不符合的人混進來。
            if (intent.MinAge.HasValue ||
                intent.MaxAge.HasValue ||
                !string.IsNullOrWhiteSpace(
                    intent.Gender))
            {
                return query.ToList();
            }

            List<string> keywords =
                ExtractSearchKeywords(
                    question
                );

            return query
                .Select(patient => new
                {
                    Patient = patient,
                    Score =
                        CalculatePatientMatchScore(
                            patient,
                            question,
                            keywords
                        )
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x =>
                    x.Score
                )
                .Select(x => x.Patient)
                .ToList();
        }

        private string BuildPatientFilterAnswer(
            AiIntentResult intent,
            List<Cadaver> matchedPatients)
        {
            if (matchedPatients.Count == 0)
            {
                return
                    "目前 HBase 病歷中沒有符合條件的病人。";
            }

            string conditionText =
                "";

            var conditions =
                new List<string>();

            if (intent.MinAge.HasValue)
            {
                conditions.Add(
                    $"{intent.MinAge.Value} 歲以上"
                );
            }

            if (intent.MaxAge.HasValue)
            {
                conditions.Add(
                    $"{intent.MaxAge.Value} 歲以下"
                );
            }

            if (!string.IsNullOrWhiteSpace(
                    intent.Gender))
            {
                conditions.Add(
                    intent.Gender == "男"
                        ? "男性"
                        : "女性"
                );
            }

            if (conditions.Count > 0)
            {
                conditionText =
                    "符合「" +
                    string.Join(
                        "、",
                        conditions
                    )
                    +
                    "」的";
            }

            string patientList =
                string.Join(
                    "、",
                    matchedPatients
                        .Select(patient =>
                        {
                            string age =
                                string.IsNullOrWhiteSpace(
                                    patient.Age)
                                    ? "年齡未提供"
                                    : $"{patient.Age} 歲";

                            string gender =
                                string.IsNullOrWhiteSpace(
                                    patient.Gender)
                                    ? "性別未提供"
                                    : patient.Gender;

                            return
                                $"{patient.RowKey}（{gender}，{age}）";
                        })
                );

            return
                $"目前{conditionText}病人共有 {matchedPatients.Count} 位：{patientList}。";
        }


        private string? TryAnswerHypotheticalRangeQuestion(
            string question,
            List<Cadaver> patients)
        {
            bool isHypothetical =
                question.Contains(
                    "如果",
                    StringComparison.OrdinalIgnoreCase
                )
                ||
                question.Contains(
                    "假設",
                    StringComparison.OrdinalIgnoreCase
                )
                ||
                question.Contains(
                    "假如",
                    StringComparison.OrdinalIgnoreCase
                );

            if (!isHypothetical)
                return null;

            Match numberMatch = Regex.Match(
                question,
                @"(?:如果|假設|假如)?\s*" +
                @"(?:數值)?\s*" +
                @"(?:是|為)?\s*" +
                @"(-?\d+(?:\.\d+)?)",
                RegexOptions.IgnoreCase
            );

            if (!numberMatch.Success ||
                !double.TryParse(
                    numberMatch.Groups[1].Value,
                    out double hypotheticalValue
                ))
            {
                return null;
            }

            List<string> availableRanges =
                patients
                    .SelectMany(patient =>
                        patient.LabTestRecords
                        ?? new List<LabTestRecord>()
                    )
                    .Where(record =>
                        !string.IsNullOrWhiteSpace(
                            record.ReferenceRange
                        )
                    )
                    .Select(record =>
                        record.ReferenceRange.Trim()
                    )
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToList();

            if (availableRanges.Count == 0)
            {
                return
                    "目前病歷資料中沒有可用的參考範圍，無法進行假設性比較。";
            }

            if (availableRanges.Count > 1)
            {
                return
                    "目前病歷資料中存在多個不同的參考範圍，請指定要比較的檢驗項目。";
            }

            string rangeText = availableRanges[0];

            Match rangeMatch = Regex.Match(
                rangeText,
                @"(-?\d+(?:\.\d+)?)\s*[–—－~\-]\s*(-?\d+(?:\.\d+)?)"
            );

            if (!rangeMatch.Success ||
                !double.TryParse(
                    rangeMatch.Groups[1].Value,
                    out double lower
                )
                ||
                !double.TryParse(
                    rangeMatch.Groups[2].Value,
                    out double upper
                ))
            {
                return
                    $"目前參考範圍為 {rangeText}，但其格式無法解析，因此無法進行數值比較。";
            }

            bool isWithin =
                hypotheticalValue >= lower &&
                hypotheticalValue <= upper;

            if (isWithin)
            {
                return
                    $"假設數值為 {hypotheticalValue:g}，" +
                    $"而目前可用的參考範圍為 {rangeText}，" +
                    $"則 {hypotheticalValue:g} 位於該參考範圍內。" +
                    "上下限均包含在範圍中。" +
                    "這是假設性比較，不代表病人的實際檢驗結果。";
            }

            string direction =
                hypotheticalValue < lower
                    ? "低於參考範圍下限"
                    : "高於參考範圍上限";

            return
                $"假設數值為 {hypotheticalValue:g}，" +
                $"而目前可用的參考範圍為 {rangeText}，" +
                $"則 {hypotheticalValue:g} {direction}，" +
                "不位於該參考範圍內。" +
                "這是假設性比較，不代表病人的實際檢驗結果。";
        }
        // =========================
        // 讀取全部資料
        // =========================
        private async Task<List<Cadaver>> GetAllData()
        {
            var result = new List<Cadaver>();

            try
            {
                // 最多等 10 秒，避免 HBase REST 卡住導致 MVC 整個爆掉
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

                var request = new HttpRequestMessage(HttpMethod.Get, "cadaver/*");
                request.Headers.Add("Accept", "application/json");

                var response = await _client.SendAsync(request, cts.Token);

                if (!response.IsSuccessStatusCode)
                    return result;

                var json = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(json))
                    return result;

                using JsonDocument doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("Row", out JsonElement rows))
                    return result;

                foreach (var row in rows.EnumerateArray())
                {
                    string rowKey = Decode(row.GetProperty("key").GetString() ?? "");

                    var cadaver = new Cadaver
                    {
                        RowKey = rowKey
                    };

                    if (!row.TryGetProperty("Cell", out JsonElement cells))
                    {
                        result.Add(cadaver);
                        continue;
                    }

                    // 先收集資料
                    Dictionary<string, string> noteMap = new();
                    Dictionary<string, bool> aliveMap = new();
                    Dictionary<string, string> imageMap = new();
                    Dictionary<string, string> videoMap = new();
                    Dictionary<string, string> videoTypeMap = new();
                    Dictionary<string, string> categoryMap = new();
                    Dictionary<string, string> drawingMap = new();
                    Dictionary<string, string> lifePhotoMap = new();
                    Dictionary<string, string> lifePhotoNoteMap = new();


                    // =========================
                    // 課程 / 解剖影像
                    // =========================
                    Dictionary<string, string>
     courseImageMap = new();

                    Dictionary<string, string>
                        courseImageNoteMap = new();

                    Dictionary<string, string>
                        courseDrawingMap = new();

                    Dictionary<string, string>
                        courseAnnotatedImageMap = new();


                    // =========================
                    // 病歷 / 病理影像
                    // =========================
                    Dictionary<string, string>
                        medicalImageMap = new();

                    Dictionary<string, string>
                        medicalImageNoteMap = new();

                    Dictionary<string, string>
                    medicalImageExaminationTypeMap = new();

                    Dictionary<string, string>
                        medicalDrawingMap = new();

                    Dictionary<string, string>
                        medicalAnnotatedImageMap = new();


                    Dictionary<string, string>
                        medicalRecordMap = new();

                    Dictionary<string, LabTestRecord>
                        labRecordMap = new();
                    foreach (var cell in cells.EnumerateArray())
                    {
                        string column =
                            Decode(
                                cell.GetProperty("column")
                                    .GetString() ?? ""
                            );

                        // 圖片 binary 不應當成 UTF-8 文字解析
                        if (column.StartsWith(
                                "medical_image:data_",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string value =
                            Decode(
                                cell.GetProperty("$")
                                    .GetString() ?? ""
                            );

                        if (column == "patient:id")
                        {
                            cadaver.PatientId = value;
                        }
                        else if (column == "patient:medical_record_number")
                        {
                            cadaver.MedicalRecordNumber = value;
                        }
                        else if (column == "patient:gender" || column == "info:gender")
                        {
                            cadaver.Gender = value;
                        }
                        else if (column == "patient:age" || column == "info:age")
                        {
                            cadaver.Age = value;
                        }
                        else if (column == "patient:blood_type")
                        {
                            cadaver.BloodType = value;
                        }
                        else if (column == "patient:blood_type_source")
                        {
                            cadaver.BloodTypeSource = value;
                        }
                        else if (column == "patient:drug_allergy_history")
                        {
                            cadaver.DrugAllergyHistory = value;
                        }
                        else if (column == "patient:past_medical_history")
                        {
                            cadaver.PastMedicalHistory = value;
                        }
                        else if (column == "patient:donation_date")
                        {
                            cadaver.DonationDate = value;
                            cadaver.DonationYear = value;
                        }
                        else if (column == "info:donation_year" ||
                                 column == "info:donate_year")
                        {
                            cadaver.DonationYear = value;

                            if (string.IsNullOrWhiteSpace(cadaver.DonationDate))
                            {
                                cadaver.DonationDate = value;
                            }
                        }
                        else if (column == "patient:dissection_date" ||
                                 column == "info:dissection_date")
                        {
                            cadaver.DissectionDate = value;
                        }
                        else if (column == "patient:dissection_status")
                        {
                            cadaver.IsDissected =
                                value == "1" ||
                                value.Equals(
                                    "true",
                                    StringComparison.OrdinalIgnoreCase
                                ) ||
                                value == "已解剖";
                        }
                        else if (column == "patient:nhis_patient_id")
                        {
                            cadaver.NhisPatientId = value;
                        }
                        else if (column == "patient:birth_year_month")
                        {
                            cadaver.BirthYearMonth = value;
                        }
                        else if (column == "patient:data_source")
                        {
                            cadaver.DataSource = value;
                        }
                        else if (column == "patient:data_version")
                        {
                            cadaver.DataVersion = value;
                        }

                        // lab_test:* 檢驗資料
                        else if (column.StartsWith("lab_test:record_"))
                        {
                            string remaining =
    column.Substring(
        "lab_test:record_".Length
    );

                            string[] fieldNames =
                            {
    "specimen_collected_at",
    "waveform_result_path",
    "image_result_path",
    "audio_result_path",
    "dicom_result_path",
    "video_result_path",
    "report_file_path",
    "examination_type",
    "reference_range",
    "test_item_code",
    "test_item_name",
    "numeric_result",
    "text_result",
    "abnormal_flag",
    "test_method",
    "test_result",
    "result_type",
    "body_part",
    "interpretation",
    "test_date",
    "test_item",
    "unit"
};

                            string fieldName =
                                fieldNames.FirstOrDefault(field =>
                                    remaining.EndsWith(
                                        "_" + field,
                                        StringComparison.OrdinalIgnoreCase
                                    )
                                ) ?? "";

                            if (!string.IsNullOrWhiteSpace(fieldName))
                            {
                                string recordId =
                                    remaining.Substring(
                                        0,
                                        remaining.Length
                                        - fieldName.Length
                                        - 1
                                    );

                                if (!labRecordMap.ContainsKey(recordId))
                                {
                                    labRecordMap[recordId] =
                                        new LabTestRecord
                                        {
                                            Id = recordId
                                        };
                                }

                                LabTestRecord record =
                                    labRecordMap[recordId];

                                switch (fieldName)
                                {
                                    case "test_date":
                                        record.TestDate = value;
                                        break;

                                    case "test_item":
                                        record.TestItem = value;
                                        break;

                                    case "test_result":
                                        record.TestResult = value;
                                        break;

                                    case "test_item_code":
                                        record.TestItemCode = value;
                                        break;

                                    case "test_item_name":
                                        record.TestItemName = value;
                                        break;

                                    case "result_type":
                                        record.ResultType = value;
                                        break;

                                    case "numeric_result":
                                        record.NumericResult = value;
                                        break;

                                    case "text_result":
                                        record.TextResult = value;
                                        break;

                                    case "image_result_path":
                                        record.ImageResultPath = value;
                                        break;

                                    case "waveform_result_path":
                                        record.WaveformResultPath = value;
                                        break;

                                    case "audio_result_path":
                                        record.AudioResultPath = value;
                                        break;

                                    case "examination_type":
                                        record.ExaminationType = value;
                                        break;

                                    case "body_part":
                                        record.BodyPart = value;
                                        break;

                                    case "interpretation":
                                        record.Interpretation = value;
                                        break;

                                    case "dicom_result_path":
                                        record.DicomResultPath = value;
                                        break;

                                    case "video_result_path":
                                        record.VideoResultPath = value;
                                        break;

                                    case "report_file_path":
                                        record.ReportFilePath = value;
                                        break;

                                    case "unit":
                                        record.Unit = value;
                                        break;

                                    case "reference_range":
                                        record.ReferenceRange = value;
                                        break;

                                    case "abnormal_flag":
                                        record.AbnormalFlag = value;
                                        break;

                                    case "test_method":
                                        record.TestMethod = value;
                                        break;

                                    case "specimen_collected_at":
                                        record.SpecimenCollectedAt = value;
                                        break;
                                }
                            }
                        }
                        else if (column == "lab_test:test_date")
                        {
                            cadaver.LabTestDate = value;
                        }
                        else if (column == "lab_test:test_item")
                        {
                            cadaver.LabTestItem = value;
                        }
                        else if (column == "lab_test:test_result")
                        {
                            cadaver.LabTestResult = value;
                        }
                        else if (column == "lab_test:test_item_code")
                        {
                            cadaver.LabTestItemCode = value;
                        }
                        else if (column == "lab_test:test_item_name")
                        {
                            cadaver.LabTestItemName = value;
                        }
                        else if (column == "lab_test:result_type")
                        {
                            cadaver.LabResultType = value;
                        }
                        else if (column == "lab_test:numeric_result")
                        {
                            cadaver.LabNumericResult = value;
                        }
                        else if (column == "lab_test:text_result")
                        {
                            cadaver.LabTextResult = value;
                        }
                        else if (column == "lab_test:image_result_path")
                        {
                            cadaver.LabImageResultPath = value;
                        }
                        else if (column == "lab_test:waveform_result_path")
                        {
                            cadaver.LabWaveformResultPath = value;
                        }
                        else if (column == "lab_test:audio_result_path")
                        {
                            cadaver.LabAudioResultPath = value;
                        }
                        else if (column == "lab_test:unit")
                        {
                            cadaver.LabUnit = value;
                        }
                        else if (column == "lab_test:reference_range")
                        {
                            cadaver.LabReferenceRange = value;
                        }
                        else if (column == "lab_test:abnormal_flag")
                        {
                            cadaver.LabAbnormalFlag = value;
                        }
                        else if (column == "lab_test:test_method")
                        {
                            cadaver.LabTestMethod = value;
                        }
                        else if (column == "lab_test:specimen_collected_at")
                        {
                            cadaver.LabSpecimenCollectedAt = value;
                        }
                        // admission:* 住院資料
                        else if (column == "admission:start_date")
                        {
                            cadaver.AdmissionStartDate = value;
                        }
                        else if (column == "admission:end_date")
                        {
                            cadaver.AdmissionEndDate = value;
                        }
                        else if (column == "admission:ward_type")
                        {
                            cadaver.WardType = value;
                        }
                        else if (column == "admission:admission_reason")
                        {
                            cadaver.AdmissionReason = value;
                        }
                        else if (column == "admission:admission_reason_code")
                        {
                            cadaver.AdmissionReasonCode = value;
                        }
                        else if (column == "admission:admission_diagnosis_code")
                        {
                            cadaver.AdmissionDiagnosisCode = value;
                        }
                        else if (column == "admission:discharge_diagnosis_code")
                        {
                            cadaver.DischargeDiagnosisCode = value;
                        }
                        else if (column == "admission:primary_diagnosis_code")
                        {
                            cadaver.PrimaryDiagnosisCode = value;
                        }
                        else if (column == "admission:secondary_diagnosis_code")
                        {
                            cadaver.SecondaryDiagnosisCode = value;
                        }
                        else if (column == "admission:hospital_code")
                        {
                            cadaver.HospitalCode = value;
                        }
                        else if (column == "admission:department_code")
                        {
                            cadaver.DepartmentCode = value;
                        }
                        else if (column == "admission:discharge_status")
                        {
                            cadaver.DischargeStatus = value;
                        }
                        else if (column == "admission:length_of_stay")
                        {
                            cadaver.LengthOfStay = value;
                        }
                        else if (column == "admission:icu_days")
                        {
                            cadaver.IcuDays = value;
                        }
                        else if (column == "admission:inpatient_order_code")
                        {
                            cadaver.InpatientOrderCode = value;
                        }
                        else if (column == "admission:procedure_code")
                        {
                            cadaver.ProcedureCode = value;
                        }
                        // teaching:* 大體解剖上課資料

                        else if (column == "teaching:dissection_process_record")
                        {
                            cadaver.DissectionProcessRecord = value;
                        }
                        else if (column == "teaching:teacher_explanation_record")
                        {
                            cadaver.TeacherExplanationRecord = value;
                        }
                        else if (column == "teaching:teaching_note")
                        {
                            cadaver.TeachingNote = value;
                        }
                        else if (column == "teaching:pathological_findings")
                        {
                            cadaver.PathologicalFindings = value;
                        }
                        else if (column == "teaching:related_diagnosis_code")
                        {
                            cadaver.RelatedDiagnosisCode = value;
                        }
                        else if (column == "teaching:related_test_item_code")
                        {
                            cadaver.RelatedTestItemCode = value;
                        }
                        else if (column == "teaching:related_order_code")
                        {
                            cadaver.RelatedOrderCode = value;
                        }


                        // info:* 原本影片分類

                        else if (column.StartsWith("info:category_"))
                        {
                            string id = column.Replace("info:category_", "");
                            categoryMap[id] = value;
                        }


                        // info:* 原本圖片 / 影片說明

                        else if (column.StartsWith("info:note_"))
                        {
                            string id = column.Replace("info:note_", "");
                            noteMap[id] = value;
                        }

                        // info:* 是否為生前影像

                        else if (column.StartsWith("info:isAlive_"))
                        {
                            string id = column.Replace("info:isAlive_", "");
                            aliveMap[id] = value == "1" ||
                                           value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        }

                        // =========================
                        // course_image:*
                        // 課程 / 解剖影像
                        // =========================

                        else if (
                            column.StartsWith(
                                "course_image:image_"))
                        {
                            string id =
                                column.Replace(
                                    "course_image:image_",
                                    ""
                                );

                            courseImageMap[id] =
                                value;
                        }

                        else if (
                            column.StartsWith(
                                "course_image:note_"))
                        {
                            string id =
                                column.Replace(
                                    "course_image:note_",
                                    ""
                                );

                            courseImageNoteMap[id] =
                                value;
                        }

                        else if (
                            column.StartsWith(
                                "course_image:drawing_"))
                        {
                            string id =
                                column.Replace(
                                    "course_image:drawing_",
                                    ""
                                );

                            courseDrawingMap[id] =
                                value;
                        }
                        else if (
                           column.StartsWith(
                               "course_image:anno_img_"))
                        {
                            string id =
                                column.Replace(
                                    "course_image:anno_img_",
                                    ""
                                );

                            courseAnnotatedImageMap[id] =
                                value;
                        }
                        // =========================
                        // medical_image:*
                        // 病歷 / 病理影像
                        // =========================

                        else if (
                            column.StartsWith(
                                "medical_image:image_"))
                        {
                            string id =
                                column.Replace(
                                    "medical_image:image_",
                                    ""
                                );

                            if (
                                value.StartsWith(
                                    "/medical-images/kaggle/",
                                    StringComparison.OrdinalIgnoreCase
                                )
                                ||
                                id.Equals(
                                    "66e7b5c001f84c0ab1ff5dc305cb25fd",
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            {
                                continue;
                            }

                            medicalImageMap[id] =
                                value;
                        }

                        else if (
                            column.StartsWith(
                                "medical_image:note_"))
                        {
                            string id =
                                column.Replace(
                                    "medical_image:note_",
                                    ""
                                );

                            medicalImageNoteMap[id] =
                                value;
                        }
                        else if (
                        column.StartsWith(
                        "medical_image:examination_type_"))
                        {
                            string id =
                                column.Replace(
                                    "medical_image:examination_type_",
                                    ""
                                );

                            medicalImageExaminationTypeMap[id] =
                                value;
                        }
                        else if (
                            column.StartsWith(
                                "medical_image:drawing_"))
                        {
                            string id =
                                column.Replace(
                                    "medical_image:drawing_",
                                    ""
                                );

                            medicalDrawingMap[id] =
                                value;
                        }
                        else if (
                            column.StartsWith(
                                "medical_image:anno_img_"))
                        {
                            string id =
                                column.Replace(
                                    "medical_image:anno_img_",
                                    ""
                                );

                            medicalAnnotatedImageMap[id] =
                                value;
                        }

                        else if (column.StartsWith(
    "info:life_photo_note_",
    StringComparison.OrdinalIgnoreCase))
                        {
                            string id =
                                column.Replace(
                                    "info:life_photo_note_",
                                    "",
                                    StringComparison.OrdinalIgnoreCase
                                );

                            lifePhotoNoteMap[id] = value;
                        }
                        else if (column.StartsWith(
                            "info:life_photo_",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            string id =
                                column.Replace(
                                    "info:life_photo_",
                                    "",
                                    StringComparison.OrdinalIgnoreCase
                                );

                            lifePhotoMap[id] = value;
                        }

                        // info:* 圖片路徑

                        else if (column.StartsWith("info:image_"))
                        {
                            string id = column.Replace("info:image_", "");
                            imageMap[id] = value;
                        }


                        // info:* 影片路徑

                        else if (column.StartsWith("info:video_type_"))
                        {
                            string id =
                                column.Replace(
                                    "info:video_type_",
                                    ""
                                );

                            videoTypeMap[id] = value;
                        }
                        else if (column.StartsWith("info:video_"))
                        {
                            string id =
                                column.Replace(
                                    "info:video_",
                                    ""
                                );

                            videoMap[id] = value;
                        }


                        // info:* 圖片畫圖標註 JSON

                        else if (column.StartsWith("info:drawing_"))
                        {
                            string id = column.Replace("info:drawing_", "");
                            drawingMap[id] = value;
                        }


                        // info:fhir_* 原本 FHIR-like 病人快照

                        else if (column == "info:fhir_patient_json" || column == "fhir:patient_json")
                        {
                            cadaver.PatientFhirJson = value;
                        }
                        else if (column == "info:fhir_patient_updated_at" || column == "fhir:patient_updated_at")
                        {
                            cadaver.PatientLastUpdated = value;
                        }

                        // info:fhir_record_* 原本 FHIR-like 病歷

                        else if (column.StartsWith("info:fhir_record_") || column.StartsWith("fhir:record_"))
                        {
                            string id = column.StartsWith("info:fhir_record_")
                                ? column.Replace("info:fhir_record_", "")
                                : column.Replace("fhir:record_", "");

                            medicalRecordMap[id] = value;
                        }
                    }

                    // 組圖片資料：路徑 / 說明 / 生前影像 / imageId / drawing
                    foreach (var kv in imageMap.OrderBy(x => x.Key))
                    {
                        string id = kv.Key;
                        cadaver.ImagePaths.Add(kv.Value);
                        cadaver.ImageIds.Add(id);
                        cadaver.Notes.Add(noteMap.ContainsKey(id) ? noteMap[id] : "");
                        cadaver.IsAliveImages.Add(aliveMap.ContainsKey(id) && aliveMap[id]);
                        cadaver.DrawingJsonList.Add(drawingMap.ContainsKey(id) ? drawingMap[id] : "[]");
                    }

                    foreach (var kv in lifePhotoMap.OrderBy(x => x.Key))
                    {
                        string id = kv.Key;

                        cadaver.LifePhotoIds.Add(id);
                        cadaver.LifePhotoPaths.Add(kv.Value);

                        cadaver.LifePhotoNotes.Add(
                            lifePhotoNoteMap.ContainsKey(id)
                                ? lifePhotoNoteMap[id]
                                : ""
                        );
                    }

                    // =========================
                    // 課程 / 解剖影像
                    // =========================

                    foreach (
                        var kv
                        in courseImageMap
                            .OrderBy(x => x.Key))
                    {
                        string id =
                            kv.Key;

                        cadaver.CourseImagePaths.Add(
                            kv.Value
                        );

                        cadaver.CourseImageIds.Add(
                            id
                        );

                        cadaver.CourseImageNotes.Add(
                            courseImageNoteMap
                                .ContainsKey(id)
                                    ? courseImageNoteMap[id]
                                    : ""
                        );

                        cadaver.CourseDrawingJsonList.Add(
                            courseDrawingMap
                                .ContainsKey(id)
                                    ? courseDrawingMap[id]
                                    : "[]"
                        );
                        cadaver.CourseAnnotatedImagePaths.Add(
                          courseAnnotatedImageMap
                            .ContainsKey(id)
                               ? courseAnnotatedImageMap[id]
                               : ""
                        );
                    }
                    // =========================
                    // 病歷 / 病理影像
                    // =========================

                    foreach (
                        var kv
                        in medicalImageMap
                            .OrderBy(x => x.Key))
                    {
                        string id =
                            kv.Key;

                        cadaver.MedicalImagePaths.Add(
                            kv.Value
                        );

                        cadaver.MedicalImageIds.Add(
                            id
                        );

                        cadaver.MedicalImageNotes.Add(
                            medicalImageNoteMap
                                .ContainsKey(id)
                                    ? medicalImageNoteMap[id]
                                    : ""
                        );

                        cadaver.MedicalImageExaminationTypes.Add(
                        medicalImageExaminationTypeMap.ContainsKey(id)
                        ? medicalImageExaminationTypeMap[id]
                                   : ""
                        );

                        cadaver.MedicalDrawingJsonList.Add(
                            medicalDrawingMap
                                .ContainsKey(id)
                                    ? medicalDrawingMap[id]
                                    : "[]"
                        );
                        cadaver.MedicalAnnotatedImagePaths.Add(
                            medicalAnnotatedImageMap
                                .ContainsKey(id)
                                   ? medicalAnnotatedImageMap[id]
                                   : ""
                         );
                    }

                    foreach (var kv in videoMap.OrderBy(x => x.Key))
                    {
                        string id = kv.Key;

                        cadaver.VideoPaths.Add(
                            kv.Value
                        );

                        cadaver.VideoNotes.Add(
                            noteMap.ContainsKey(id)
                                ? noteMap[id]
                                : ""
                        );

                        cadaver.VideoCategories.Add(
                            categoryMap.ContainsKey(id)
                                ? categoryMap[id]
                                : "未分類"
                        );

                        cadaver.VideoIsAliveImages.Add(
                            aliveMap.ContainsKey(id) &&
                            aliveMap[id]
                        );

                        cadaver.VideoTypes.Add(
                            videoTypeMap.ContainsKey(id)
                                ? videoTypeMap[id]
                                : "course"
                        );
                    }

                    cadaver.MedicalRecords = medicalRecordMap
                        .Select(x => ParseMedicalRecord(x.Key, x.Value))
                        .OrderByDescending(x =>
                            DateTime.TryParse(x.OccurredAt, out var d)
                                ? d
                                : DateTime.MinValue)
                        .ToList();

                    cadaver.LabTestRecords = labRecordMap.Values
                        .OrderByDescending(x =>
                            DateTime.TryParse(x.TestDate, out var d)
                                ? d
                                : DateTime.MinValue)
                        .ToList();

                    // 相容舊版單筆 lab_test:* 欄位
                    bool hasLegacyLabData =
                        !string.IsNullOrWhiteSpace(cadaver.LabTestDate)
                        ||
                        !string.IsNullOrWhiteSpace(cadaver.LabTestItem)
                        ||
                        !string.IsNullOrWhiteSpace(cadaver.LabTestResult)
                        ||
                        !string.IsNullOrWhiteSpace(cadaver.LabTestItemCode)
                        ||
                        !string.IsNullOrWhiteSpace(cadaver.LabTestItemName)
                        ||
                        !string.IsNullOrWhiteSpace(cadaver.LabNumericResult)
                        ||
                        !string.IsNullOrWhiteSpace(cadaver.LabTextResult);

                    if (hasLegacyLabData)
                    {
                        bool alreadyExists =
                            cadaver.LabTestRecords.Any(record =>
                                string.Equals(
                                    record.TestDate,
                                    cadaver.LabTestDate,
                                    StringComparison.OrdinalIgnoreCase
                                )
                                &&
                                string.Equals(
                                    record.TestItem,
                                    cadaver.LabTestItem,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            );

                        if (!alreadyExists)
                        {
                            cadaver.LabTestRecords.Add(
                                new LabTestRecord
                                {
                                    Id = "legacy",
                                    TestDate = cadaver.LabTestDate,
                                    TestItem = cadaver.LabTestItem,
                                    TestResult = cadaver.LabTestResult,
                                    TestItemCode = cadaver.LabTestItemCode,
                                    TestItemName = cadaver.LabTestItemName,
                                    ResultType = cadaver.LabResultType,
                                    NumericResult = cadaver.LabNumericResult,
                                    TextResult = cadaver.LabTextResult,
                                    ImageResultPath =
                                        cadaver.LabImageResultPath,
                                    WaveformResultPath =
                                        cadaver.LabWaveformResultPath,
                                    AudioResultPath =
                                        cadaver.LabAudioResultPath,
                                    Unit = cadaver.LabUnit,
                                    ReferenceRange =
                                        cadaver.LabReferenceRange,
                                    AbnormalFlag =
                                        cadaver.LabAbnormalFlag,
                                    TestMethod =
                                        cadaver.LabTestMethod,
                                    SpecimenCollectedAt =
                                        cadaver.LabSpecimenCollectedAt
                                }
                            );
                        }

                        cadaver.LabTestRecords =
                            cadaver.LabTestRecords
                                .OrderByDescending(record =>
                                    DateTime.TryParse(
                                        record.TestDate,
                                        out DateTime date)
                                        ? date
                                        : DateTime.MinValue
                                )
                                .ToList();
                    }
                    result.Add(cadaver);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "讀取 HBase cadaver 資料失敗。BaseAddress={BaseAddress}",
                    _client.BaseAddress
                );

                return result;
            }
        }

        // HBase 新增 / 修改欄位
        // HBase 新增 / 修改單一欄位
        private async Task PutCell(
            string row,
            string column,
            string value)
        {
            string xml = $@"
        <CellSet>
          <Row key=""{ToBase64(row)}"">
          <Cell column=""{ToBase64(column)}"">
        {ToBase64(value)}
        </Cell>
        </Row>
      </CellSet>";

            var content =
                new StringContent(
                    xml,
                    Encoding.UTF8,
                    "text/xml"
                );

            HttpResponseMessage response =
                await _client.PutAsync(
                    $"cadaver/{Uri.EscapeDataString(row)}",
                    content
                );

            response.EnsureSuccessStatusCode();
        }


        // HBase 刪除欄位
        private async Task DeleteCell(
            string row,
            string column)
        {
            string url =
                $"cadaver/{Uri.EscapeDataString(row)}/" +
                $"{Uri.EscapeDataString(column)}";

            HttpResponseMessage response =
                await _client.DeleteAsync(url);

            if (!response.IsSuccessStatusCode &&
                response.StatusCode !=
                    System.Net.HttpStatusCode.NotFound)
            {
                response.EnsureSuccessStatusCode();
            }
        }

        [HttpGet]
        public async Task<IActionResult> LifePhotos(string row)
        {
            var block = RequireLogin();

            if (block != null)
                return block;

            row = row?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(row))
                return RedirectToAction("Index");

            List<Cadaver> data =
                await GetAllData();

            Cadaver? patient =
                data.FirstOrDefault(x =>
                    string.Equals(
                        x.RowKey,
                        row,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (patient == null)
            {
                TempData["Message"] =
                    "找不到指定的大體老師資料。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "Index",
                    new { panel = "list" }
                );
            }

            return View(patient);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadLifePhoto(
    string row,
    IFormFile photo,
    string note)
        {
            var block = RequireAdmin();

            if (block != null)
                return block;

            row = row?.Trim() ?? "";
            note = note?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(row))
            {
                TempData["Message"] =
                    "缺少大體老師編號。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction("Index");
            }

            if (photo == null ||
                photo.Length == 0)
            {
                TempData["Message"] =
                    "請選擇照片。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "LifePhotos",
                    new { row }
                );
            }

            string extension =
                Path.GetExtension(photo.FileName)
                    .ToLowerInvariant();

            string[] allowedExtensions =
            {
        ".jpg",
        ".jpeg",
        ".png"
    };

            if (!allowedExtensions.Contains(extension))
            {
                TempData["Message"] =
                    "只允許上傳 JPG、JPEG、PNG。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "LifePhotos",
                    new { row }
                );
            }

            // 10 MB
            if (photo.Length > 10 * 1024 * 1024)
            {
                TempData["Message"] =
                    "圖片大小不可超過 10 MB。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "LifePhotos",
                    new { row }
                );
            }

            string id =
                Guid.NewGuid()
                    .ToString("N");

            string folder =
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "life-photos",
                    row
                );

            Directory.CreateDirectory(folder);

            string fileName =
                id + extension;

            string fullPath =
                Path.Combine(
                    folder,
                    fileName
                );

            using (FileStream stream =
                new FileStream(
                    fullPath,
                    FileMode.Create))
            {
                await photo.CopyToAsync(stream);
            }

            string webPath =
                $"/life-photos/{row}/{fileName}";

            await PutCell(
                row,
                $"info:life_photo_{id}",
                webPath
            );

            await PutCell(
                row,
                $"info:life_photo_note_{id}",
                note
            );

            TempData["Message"] =
                "生前照片新增成功。";

            TempData["MessageType"] =
                "success";

            return RedirectToAction(
                "LifePhotos",
                new { row }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateLifePhoto(
    string row,
    string photoId,
    string note)
        {
            var block = RequireAdmin();

            if (block != null)
                return block;

            row = row?.Trim() ?? "";
            photoId = photoId?.Trim() ?? "";
            note = note?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(row) ||
                string.IsNullOrWhiteSpace(photoId))
            {
                return BadRequest();
            }

            await PutCell(
                row,
                $"info:life_photo_note_{photoId}",
                note
            );

            TempData["Message"] =
                "生前照片資料修改成功。";

            TempData["MessageType"] =
                "success";

            return RedirectToAction(
                "LifePhotos",
                new { row }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteLifePhoto(
    string row,
    string photoId,
    string photoPath)
        {
            var block = RequireAdmin();

            if (block != null)
                return block;

            row = row?.Trim() ?? "";
            photoId = photoId?.Trim() ?? "";
            photoPath = photoPath?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(row) ||
                string.IsNullOrWhiteSpace(photoId))
            {
                return BadRequest();
            }

            await DeleteCell(
                row,
                $"info:life_photo_{photoId}"
            );

            await DeleteCell(
                row,
                $"info:life_photo_note_{photoId}"
            );

            // 刪除實體圖片
            if (!string.IsNullOrWhiteSpace(photoPath))
            {
                try
                {
                    string relativePath =
                        photoPath
                            .TrimStart('/')
                            .Replace(
                                '/',
                                Path.DirectorySeparatorChar
                            );

                    string wwwroot =
                        Path.GetFullPath(
                            Path.Combine(
                                Directory.GetCurrentDirectory(),
                                "wwwroot"
                            )
                        );

                    string fullPath =
                        Path.GetFullPath(
                            Path.Combine(
                                wwwroot,
                                relativePath
                            )
                        );

                    if (fullPath.StartsWith(
                            wwwroot,
                            StringComparison.OrdinalIgnoreCase)
                        &&
                        System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "刪除生前照片實體檔案失敗。Row={Row}",
                        row
                    );
                }
            }

            TempData["Message"] =
                "生前照片刪除成功。";

            TempData["MessageType"] =
                "success";

            return RedirectToAction(
                "LifePhotos",
                new { row }
            );
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ForgotPassword(
    string role,
    string loginId,
    string email,
    string newPassword,
    string confirmPassword)
        {
            role = role?.Trim() ?? "";
            loginId = loginId?.Trim() ?? "";
            email = email?.Trim() ?? "";
            newPassword = newPassword ?? "";
            confirmPassword = confirmPassword ?? "";

            if (role != "Teacher" &&
                role != "Student")
            {
                TempData["Message"] =
                    "僅老師與學生可以使用忘記密碼功能。";

                return RedirectToAction(
                    "ForgotPassword"
                );
            }

            if (string.IsNullOrWhiteSpace(loginId) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(newPassword) ||
                string.IsNullOrWhiteSpace(confirmPassword))
            {
                TempData["Message"] =
                    "所有欄位都必須輸入。";

                return RedirectToAction(
                    "ForgotPassword"
                );
            }

            if (newPassword != confirmPassword)
            {
                TempData["Message"] =
                    "兩次輸入的密碼不一致。";

                return RedirectToAction(
                    "ForgotPassword"
                );
            }

            var users = LoadUsers();

            var user = users.FirstOrDefault(x =>
                x.Role == role &&
                x.LoginId == loginId &&
                string.Equals(
                    x.Email,
                    email,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            if (user == null)
            {
                TempData["Message"] =
                    "帳號、身分或 Email 不正確。";

                return RedirectToAction(
                    "ForgotPassword"
                );
            }

            user.PasswordHash =
                HashPassword(newPassword);

            SaveUsers(users);

            TempData["Message"] =
                "密碼重設成功，請使用新密碼登入。";

            return RedirectToAction("Login");
        }

        // 登入/註冊
        private string UserFilePath =>
            Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "users.json");

        private void EnsureUserFile()
        {
            string dir = Path.GetDirectoryName(UserFilePath)!;

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!System.IO.File.Exists(UserFilePath))
            {
                var defaultUsers = new List<UserAccount>
        {
            new UserAccount
            {
                Role = "Admin",
                LoginId = "admin",
                PasswordHash = HashPassword("7GWbYGdEDY")
            }
        };

                string json = JsonSerializer.Serialize(
                    defaultUsers,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                System.IO.File.WriteAllText(UserFilePath, json);
            }
        }

        private List<UserAccount> LoadUsers()
        {
            EnsureUserFile();

            string json = System.IO.File.ReadAllText(UserFilePath);

            return JsonSerializer.Deserialize<List<UserAccount>>(json)
                   ?? new List<UserAccount>();
        }

        private void SaveUsers(List<UserAccount> users)
        {
            EnsureUserFile();

            string json = JsonSerializer.Serialize(users, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            System.IO.File.WriteAllText(UserFilePath, json);
        }

        private string HashPassword(string password)
        {
            using SHA256 sha = SHA256.Create();

            byte[] bytes = Encoding.UTF8.GetBytes(password ?? "");
            byte[] hash = sha.ComputeHash(bytes);

            return Convert.ToHexString(hash);
        }

        private string GenerateCaptcha()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            Random rnd = new Random();

            char[] code = new char[5];

            for (int i = 0; i < code.Length; i++)
                code[i] = chars[rnd.Next(chars.Length)];

            return new string(code);
        }

        private bool IsLogin()
        {
            return HttpContext.Session.GetString("LoginId") != null;
        }

        private bool IsTeacher()
        {
            string role = HttpContext.Session.GetString("Role") ?? "";

            // 管理員具備與老師相同的病理資料管理權限
            return role == "Teacher" || role == "Admin";
        }

        private bool IsAdmin()
        {
            return HttpContext.Session.GetString("Role") == "Admin";
        }

        private IActionResult? RequireLogin()
        {
            if (!IsLogin())
                return RedirectToAction("Login");

            return null;
        }

        private IActionResult? RequireTeacher()
        {
            if (!IsLogin())
                return RedirectToAction("Login");

            if (!IsTeacher())
            {
                TempData["Message"] = "學生帳號僅能瀏覽資料，沒有管理權限";
                return RedirectToAction("Index");
            }

            return null;
        }

        private IActionResult? RequireAdmin()
        {
            if (!IsLogin())
                return RedirectToAction("Login");

            if (!IsAdmin())
            {
                TempData["Message"] = "此功能僅限管理員使用";
                return RedirectToAction("Index");
            }

            return null;
        }

        // =========================
        // 登入頁
        // =========================
        [HttpGet]
        public IActionResult Login()
        {
            string code = GenerateCaptcha();
            HttpContext.Session.SetString("Captcha", code);
            ViewBag.Captcha = code;
            TempData.Remove("Message");
            TempData.Remove("MessageType");
            return View();
        }

        [HttpPost]
        public IActionResult Login(string role, string loginId, string password, string captcha)
        {
            string? realCaptcha = HttpContext.Session.GetString("Captcha");

            if (string.IsNullOrWhiteSpace(role) ||
                string.IsNullOrWhiteSpace(loginId) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(captcha))
            {
                TempData["Message"] = "所有欄位都必須輸入";
                return RedirectToAction("Login");
            }

            if (realCaptcha == null || captcha.ToUpper() != realCaptcha.ToUpper())
            {
                TempData["Message"] = "驗證碼錯誤";
                return RedirectToAction("Login");
            }

            var users = LoadUsers();

            string hash = HashPassword(password);

            var user = users.FirstOrDefault(x =>
                x.Role == role &&
                x.LoginId == loginId &&
                x.PasswordHash == hash);

            if (user == null)
            {
                TempData["Message"] = "帳號、密碼或身分錯誤";
                return RedirectToAction("Login");
            }

            HttpContext.Session.SetString("Role", user.Role);
            HttpContext.Session.SetString("LoginId", user.LoginId);

            if (user.Role == "Admin")
            {
                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }

            return RedirectToAction("Index");
        }

        // =========================
        // 註冊頁
        // =========================
        [HttpGet]
        public IActionResult Register()
        {
            string code = GenerateCaptcha();
            HttpContext.Session.SetString("Captcha", code);
            ViewBag.Captcha = code;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Register(
    string role,
    string loginId,
    string email,
    string password,
    string confirmPassword,
    string captcha)
        {
            string? realCaptcha =
                HttpContext.Session.GetString("Captcha");

            role = role?.Trim() ?? "";
            loginId = loginId?.Trim() ?? "";
            email = email?.Trim() ?? "";
            password = password ?? "";
            confirmPassword = confirmPassword ?? "";
            captcha = captcha?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(role) ||
                string.IsNullOrWhiteSpace(loginId) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password) ||
                string.IsNullOrWhiteSpace(confirmPassword) ||
                string.IsNullOrWhiteSpace(captcha))
            {
                TempData["Message"] =
                    "所有欄位都必須輸入";

                return RedirectToAction("Register");
            }

            if (realCaptcha == null ||
                captcha.ToUpper() != realCaptcha.ToUpper())
            {
                TempData["Message"] =
                    "驗證碼錯誤";

                return RedirectToAction("Register");
            }

            if (password != confirmPassword)
            {
                TempData["Message"] =
                    "兩次密碼不一致";

                return RedirectToAction("Register");
            }

            if (role != "Student")
            {
                TempData["Message"] =
                    "老師帳號必須由管理員建立";

                return RedirectToAction("Register");
            }

            if (!loginId.All(char.IsDigit))
            {
                TempData["Message"] =
                    "學生請輸入學號，只能包含數字";

                return RedirectToAction("Register");
            }

            var users = LoadUsers();

            bool exists =
                users.Any(x =>
                    x.Role == role &&
                    x.LoginId == loginId
                );

            if (exists)
            {
                TempData["Message"] =
                    "此帳號已存在";

                return RedirectToAction("Register");
            }

            users.Add(new UserAccount
            {
                Role = "Student",
                LoginId = loginId,
                Email = email,
                PasswordHash =
                    HashPassword(password)
            });

            SaveUsers(users);

            TempData["Message"] =
                "註冊成功，請登入";

            return RedirectToAction("Login");
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();

            return RedirectToAction("Login");
        }


        // 管理員：老師帳號管理
        [HttpGet]
        public IActionResult TeacherAccounts()
        {
            var block = RequireAdmin();
            if (block != null)
                return block;

            // 每次進入頁面產生新的驗證碼
            string code = GenerateCaptcha();
            HttpContext.Session.SetString("TeacherCaptcha", code);
            ViewBag.Captcha = code;

            var teachers = LoadUsers()
                .Where(x => x.Role == "Teacher")
                .OrderBy(x => x.LoginId)
                .ToList();

            return View(teachers);
        }

        // 管理員：學生帳號管理
        [HttpGet]
        public IActionResult StudentAccounts()
        {
            var block = RequireAdmin();

            if (block != null)
                return block;

            var students = LoadUsers()
                .Where(x => x.Role == "Student")
                .OrderBy(x => x.LoginId)
                .ToList();

            return View(students);
        }

        // 建立老師帳號
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateTeacherAccount(
    string loginId,
    string email,
    string password,
    string confirmPassword,
    string captcha)
        {
            var block = RequireAdmin();
            if (block != null)
                return block;

            loginId = loginId?.Trim() ?? "";
            email = email?.Trim() ?? "";
            password ??= "";
            confirmPassword ??= "";
            captcha = captcha?.Trim() ?? "";

            string? realCaptcha =
                HttpContext.Session.GetString("TeacherCaptcha");

            if (string.IsNullOrWhiteSpace(loginId) ||
    string.IsNullOrWhiteSpace(email) ||
    string.IsNullOrWhiteSpace(password) ||
    string.IsNullOrWhiteSpace(confirmPassword) ||
    string.IsNullOrWhiteSpace(captcha))
            {
                TempData["Message"] = "所有欄位都必須輸入";
                TempData["MessageType"] = "error";

                return RedirectToAction("TeacherAccounts");
            }

            if (realCaptcha == null ||
                !captcha.Equals(
                    realCaptcha,
                    StringComparison.OrdinalIgnoreCase))
            {
                TempData["Message"] = "驗證碼錯誤";
                TempData["MessageType"] = "error";

                return RedirectToAction("TeacherAccounts");
            }

            if (loginId.Length < 4)
            {
                TempData["Message"] =
                    "教職員編號長度不可少於 4 個字元";

                TempData["MessageType"] = "error";

                return RedirectToAction("TeacherAccounts");
            }

            if (password.Length < 8)
            {
                TempData["Message"] =
                    "密碼長度不可少於 8 個字元";

                TempData["MessageType"] = "error";

                return RedirectToAction("TeacherAccounts");
            }

            if (password != confirmPassword)
            {
                TempData["Message"] = "兩次輸入的密碼不一致";
                TempData["MessageType"] = "error";

                return RedirectToAction("TeacherAccounts");
            }

            var users = LoadUsers();

            bool exists = users.Any(x =>
                x.Role == "Teacher" &&
                x.LoginId.Equals(
                    loginId,
                    StringComparison.OrdinalIgnoreCase));

            if (exists)
            {
                TempData["Message"] = "此老師帳號已存在";
                TempData["MessageType"] = "error";

                return RedirectToAction("TeacherAccounts");
            }

            users.Add(new UserAccount
            {
                Role = "Teacher",
                LoginId = loginId,
                Email = email,
                PasswordHash = HashPassword(password)
            });

            SaveUsers(users);

            TempData["Message"] =
                $"老師帳號「{loginId}」建立成功";

            TempData["MessageType"] = "success";

            return RedirectToAction("TeacherAccounts");
        }



        // 修改老師帳號密碼
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ResetTeacherPassword(
            string loginId,
            string newPassword,
            string confirmNewPassword)
        {
            var block = RequireAdmin();
            if (block != null)
                return block;

            loginId = loginId?.Trim() ?? "";
            newPassword ??= "";
            confirmNewPassword ??= "";

            if (string.IsNullOrWhiteSpace(loginId) ||
                string.IsNullOrWhiteSpace(newPassword) ||
                string.IsNullOrWhiteSpace(confirmNewPassword))
            {
                TempData["Message"] = "請完整輸入新密碼";
                TempData["MessageType"] = "error";

                return RedirectToAction("TeacherAccounts");
            }

            if (newPassword.Length < 8)
            {
                TempData["Message"] =
                    "新密碼長度不可少於 8 個字元";

                TempData["MessageType"] = "error";

                return RedirectToAction("TeacherAccounts");
            }

            if (newPassword != confirmNewPassword)
            {
                TempData["Message"] =
                    "兩次輸入的新密碼不一致";

                TempData["MessageType"] = "error";

                return RedirectToAction("TeacherAccounts");
            }

            var users = LoadUsers();

            var teacher = users.FirstOrDefault(x =>
                x.Role == "Teacher" &&
                x.LoginId.Equals(
                    loginId,
                    StringComparison.OrdinalIgnoreCase));

            if (teacher == null)
            {
                TempData["Message"] = "找不到此老師帳號";
                TempData["MessageType"] = "error";

                return RedirectToAction("TeacherAccounts");
            }

            teacher.PasswordHash = HashPassword(newPassword);

            SaveUsers(users);

            TempData["Message"] =
                $"老師帳號「{teacher.LoginId}」密碼已更新";

            TempData["MessageType"] = "success";

            return RedirectToAction("TeacherAccounts");
        }



        // 刪除老師帳號
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteTeacherAccount(string loginId)
        {
            var block = RequireAdmin();
            if (block != null)
                return block;

            loginId = loginId?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(loginId))
            {
                TempData["Message"] = "缺少老師帳號";
                TempData["MessageType"] = "error";

                return RedirectToAction("TeacherAccounts");
            }

            var users = LoadUsers();

            var teacher = users.FirstOrDefault(x =>
                x.Role == "Teacher" &&
                x.LoginId.Equals(
                    loginId,
                    StringComparison.OrdinalIgnoreCase));

            if (teacher == null)
            {
                TempData["Message"] = "找不到此老師帳號";
                TempData["MessageType"] = "error";

                return RedirectToAction("TeacherAccounts");
            }

            users.Remove(teacher);
            SaveUsers(users);

            TempData["Message"] =
                $"老師帳號「{loginId}」已刪除";

            TempData["MessageType"] = "success";

            return RedirectToAction("TeacherAccounts");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult ResetStudentPassword(
    string loginId,
    string newPassword,
    string confirmNewPassword)
        {
            var block = RequireAdmin();

            if (block != null)
                return block;

            loginId =
                loginId?.Trim() ?? "";

            newPassword ??= "";
            confirmNewPassword ??= "";

            if (
                string.IsNullOrWhiteSpace(loginId) ||
                string.IsNullOrWhiteSpace(newPassword) ||
                string.IsNullOrWhiteSpace(confirmNewPassword)
            )
            {
                TempData["Message"] =
                    "請完整輸入新密碼";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "StudentAccounts"
                );
            }

            if (newPassword.Length < 8)
            {
                TempData["Message"] =
                    "新密碼長度不可少於 8 個字元";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "StudentAccounts"
                );
            }

            if (newPassword != confirmNewPassword)
            {
                TempData["Message"] =
                    "兩次輸入的新密碼不一致";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "StudentAccounts"
                );
            }

            var users =
                LoadUsers();

            var student =
                users.FirstOrDefault(x =>
                    x.Role == "Student" &&
                    x.LoginId.Equals(
                        loginId,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (student == null)
            {
                TempData["Message"] =
                    "找不到此學生帳號";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "StudentAccounts"
                );
            }

            student.PasswordHash =
                HashPassword(newPassword);

            SaveUsers(users);

            TempData["Message"] =
                $"學生帳號「{student.LoginId}」密碼已更新";

            TempData["MessageType"] =
                "success";

            return RedirectToAction(
                "StudentAccounts"
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteStudentAccount(
    string loginId)
        {
            var block =
                RequireAdmin();

            if (block != null)
                return block;

            loginId =
                loginId?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(loginId))
            {
                TempData["Message"] =
                    "缺少學生帳號";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "StudentAccounts"
                );
            }

            var users =
                LoadUsers();

            var student =
                users.FirstOrDefault(x =>
                    x.Role == "Student" &&
                    x.LoginId.Equals(
                        loginId,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (student == null)
            {
                TempData["Message"] =
                    "找不到此學生帳號";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "StudentAccounts"
                );
            }

            users.Remove(student);

            SaveUsers(users);

            TempData["Message"] =
                $"學生帳號「{loginId}」已刪除";

            TempData["MessageType"] =
                "success";

            return RedirectToAction(
                "StudentAccounts"
            );
        }

        // FHIR-like 病歷資料工具

        private MedicalRecord ParseMedicalRecord(string id, string fhirJson)
        {
            var record = new MedicalRecord
            {
                Id = id,
                FhirJson = fhirJson ?? ""
            };

            if (string.IsNullOrWhiteSpace(fhirJson))
                return record;

            try
            {
                using JsonDocument doc = JsonDocument.Parse(fhirJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("resourceType", out var resourceType))
                    record.ResourceType = resourceType.GetString() ?? "Observation";

                if (root.TryGetProperty("effectiveDateTime", out var effectiveDateTime))
                    record.OccurredAt = effectiveDateTime.GetString() ?? "";

                if (root.TryGetProperty("issued", out var issued) && string.IsNullOrWhiteSpace(record.OccurredAt))
                    record.OccurredAt = issued.GetString() ?? "";

                if (root.TryGetProperty("code", out var code) &&
                    code.TryGetProperty("text", out var codeText))
                    record.Diagnosis = codeText.GetString() ?? "";

                if (root.TryGetProperty("bodySite", out var bodySite) &&
                    bodySite.TryGetProperty("text", out var bodySiteText))
                    record.BodyPart = bodySiteText.GetString() ?? "";

                if (root.TryGetProperty("valueString", out var valueString))
                    record.Finding = valueString.GetString() ?? "";

                if (root.TryGetProperty("extension", out var extensions) && extensions.ValueKind == JsonValueKind.Array)
                {
                    foreach (var ext in extensions.EnumerateArray())
                    {
                        string url = ext.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                        string value = ext.TryGetProperty("valueString", out var valueProp) ? valueProp.GetString() ?? "" : "";

                        if (url.EndsWith("treatment", StringComparison.OrdinalIgnoreCase))
                            record.Treatment = value;
                        else if (url.EndsWith("physician", StringComparison.OrdinalIgnoreCase))
                            record.Physician = value;
                    }
                }

                if (root.TryGetProperty("note", out var notes) && notes.ValueKind == JsonValueKind.Array)
                {
                    var first = notes.EnumerateArray().FirstOrDefault();
                    if (first.ValueKind != JsonValueKind.Undefined && first.TryGetProperty("text", out var noteText))
                        record.Note = noteText.GetString() ?? "";
                }
            }
            catch
            {
                record.Note = "FHIR JSON 解析失敗，保留原始資料。";
            }

            return record;
        }

        private string BuildPatientFhirJson(string row, string gender, string age, string donationDate, string dissectionDate)
        {
            var patient = new
            {
                resourceType = "Patient",
                id = row,
                identifier = new[]
                {
            new { system = "urn:hbase:cadaver:rowkey", value = row }
        },
                gender = gender ?? "",
                extension = new object[]
                {
            new { url = "urn:hbase:cadaver:age", valueString = age ?? "" },
            new { url = "urn:hbase:cadaver:donation-date", valueString = donationDate ?? "" },
            new { url = "urn:hbase:cadaver:dissection-date", valueString = dissectionDate ?? "" }
                }
            };

            return JsonSerializer.Serialize(patient, new JsonSerializerOptions { WriteIndented = true });
        }

        private string BuildObservationFhirJson(
            string row,
            string recordId,
            string occurredAt,
            string diagnosis,
            string bodyPart,
            string finding,
            string treatment,
            string physician,
            string note)
        {
            var observation = new
            {
                resourceType = "Observation",
                id = recordId,
                status = "final",
                subject = new { reference = $"Patient/{row}" },
                effectiveDateTime = string.IsNullOrWhiteSpace(occurredAt)
                    ? DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
                    : occurredAt,
                code = new { text = diagnosis ?? "" },
                bodySite = new { text = bodyPart ?? "" },
                valueString = finding ?? "",
                extension = new object[]
                {
                    new { url = "urn:hbase:medical-record:treatment", valueString = treatment ?? "" },
                    new { url = "urn:hbase:medical-record:physician", valueString = physician ?? "" }
                },
                note = new[]
                {
                    new { text = note ?? "" }
                }
            };

            return JsonSerializer.Serialize(observation, new JsonSerializerOptions { WriteIndented = true });
        }

        private async Task SavePatientSnapshot(string row, string gender, string age, string donationDate, string dissectionDate)
        {
            string now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
            string patientJson = BuildPatientFhirJson(row, gender, age, donationDate, dissectionDate);

            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">
    <Cell column=""{ToBase64("info:fhir_patient_json")}"">{ToBase64(patientJson)}</Cell>
    <Cell column=""{ToBase64("info:fhir_patient_updated_at")}"">{ToBase64(now)}</Cell>
  </Row>
</CellSet>";

            await _client.PutAsync($"cadaver/{row}", new StringContent(xml, Encoding.UTF8, "text/xml"));
        }

        private async Task PutMedicalRecord(string row, MedicalRecord record)
        {
            if (string.IsNullOrWhiteSpace(record.Id))
                record.Id = Guid.NewGuid().ToString("N");

            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">
    <Cell column=""{ToBase64($"info:fhir_record_{record.Id}")}"">{ToBase64(record.FhirJson)}</Cell>
    <Cell column=""{ToBase64("info:fhir_last_updated")}"">{ToBase64(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"))}</Cell>
  </Row>
</CellSet>";

            await _client.PutAsync($"cadaver/{row}", new StringContent(xml, Encoding.UTF8, "text/xml"));
        }

        private async Task RestoreMedicalRecords(string row, Cadaver cadaver)
        {
            if (!string.IsNullOrWhiteSpace(cadaver.PatientFhirJson))
            {
                string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">
    <Cell column=""{ToBase64("info:fhir_patient_json")}"">{ToBase64(cadaver.PatientFhirJson)}</Cell>
    <Cell column=""{ToBase64("info:fhir_patient_updated_at")}"">{ToBase64(cadaver.PatientLastUpdated)}</Cell>
  </Row>
</CellSet>";

                await _client.PutAsync($"cadaver/{row}", new StringContent(xml, Encoding.UTF8, "text/xml"));
            }

            foreach (var record in cadaver.MedicalRecords)
            {
                await PutMedicalRecord(row, record);
            }
        }
        private async Task<List<ClassSchedule>> GetClassSchedules()
        {
            var result = new List<ClassSchedule>();

            try
            {
                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(10)
                    );

                var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        "class_schedule/*"
                    );

                request.Headers.Add(
                    "Accept",
                    "application/json"
                );

                var response =
                    await _client.SendAsync(
                        request,
                        cts.Token
                    );

                if (!response.IsSuccessStatusCode)
                    return result;

                string json =
                    await response.Content
                        .ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(json))
                    return result;

                using JsonDocument doc =
                    JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty(
                        "Row",
                        out JsonElement rows))
                {
                    return result;
                }

                foreach (var row in rows.EnumerateArray())
                {
                    string rowKey =
                        Decode(
                            row.GetProperty("key")
                                .GetString() ?? ""
                        );

                    var item =
                        new ClassSchedule
                        {
                            RowKey = rowKey
                        };

                    if (!row.TryGetProperty(
                            "Cell",
                            out JsonElement cells))
                    {
                        result.Add(item);
                        continue;
                    }

                    foreach (var cell in cells.EnumerateArray())
                    {
                        string column =
                            Decode(
                                cell.GetProperty("column")
                                    .GetString() ?? ""
                            );

                        string value =
                            Decode(
                                cell.GetProperty("$")
                                    .GetString() ?? ""
                            );

                        if (column ==
                            "schedule:class_session_id")
                        {
                            item.ClassSessionId = value;
                        }
                        else if (column ==
                                 "schedule:class_date")
                        {
                            item.ClassDate = value;
                        }
                        else if (column ==
                                 "schedule:table_id")
                        {
                            item.TableId = value;
                        }
                        else if (column ==
                                 "schedule:cadaver_row_key")
                        {
                            item.CadaverRowKey = value;
                        }
                    }

                    result.Add(item);
                }

                return result
                    .OrderByDescending(x =>
                        DateTime.TryParse(
                            x.ClassDate,
                            out DateTime date
                        )
                            ? date
                            : DateTime.MinValue
                    )
                    .ThenBy(x => x.ClassSessionId)
                    .ToList();
            }
            catch
            {
                return result;
            }
        }
        private async Task<List<TeachingApplication>>
    GetTeachingApplications()
        {
            var result =
                new List<TeachingApplication>();

            try
            {
                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(10)
                    );

                var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        "teaching_application/*"
                    );

                request.Headers.Add(
                    "Accept",
                    "application/json"
                );

                var response =
                    await _client.SendAsync(
                        request,
                        cts.Token
                    );

                if (!response.IsSuccessStatusCode)
                    return result;

                string json =
                    await response.Content
                        .ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(json))
                    return result;

                using JsonDocument doc =
                    JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty(
                        "Row",
                        out JsonElement rows))
                {
                    return result;
                }

                foreach (
                    var row
                    in rows.EnumerateArray()
                )
                {
                    string rowKey =
                        Decode(
                            row.GetProperty("key")
                                .GetString() ?? ""
                        );

                    var item =
                        new TeachingApplication
                        {
                            RowKey = rowKey
                        };

                    if (!row.TryGetProperty(
                            "Cell",
                            out JsonElement cells))
                    {
                        result.Add(item);
                        continue;
                    }

                    foreach (
                        var cell
                        in cells.EnumerateArray()
                    )
                    {
                        string column =
                            Decode(
                                cell
                                    .GetProperty(
                                        "column"
                                    )
                                    .GetString()
                                ?? ""
                            );

                        string value =
                            Decode(
                                cell
                                    .GetProperty("$")
                                    .GetString()
                                ?? ""
                            );

                        switch (column)
                        {
                            case "info:applicant":
                                item.Applicant =
                                    value;
                                break;

                            case "info:group_name":
                                item.GroupName =
                                    value;
                                break;

                            case "info:class_time":
                                item.ClassTime =
                                    value;
                                break;

                            case "info:age":
                                item.Age =
                                    value;
                                break;

                            case "info:medical_history":
                                item.MedicalHistory =
                                    value;
                                break;

                            case "info:diagnosis":
                                item.Diagnosis =
                                    value;
                                break;

                            case "info:department":
                                item.Department =
                                    value;
                                break;

                            case "info:assistant":
                                item.Assistant =
                                    value;
                                break;

                            case "info:main_doctor":
                                item.MainDoctor =
                                    value;
                                break;

                            case "info:nurse":
                                item.Nurse =
                                    value;
                                break;

                            case "info:course_content":
                                item.CourseContent =
                                    value;
                                break;

                            case "info:surgery_topic":
                                item.SurgeryTopic =
                                    value;
                                break;

                            case "info:procedure":
                                item.Procedure =
                                    value;
                                break;

                            case "info:body_part":
                                item.BodyPart =
                                    value;
                                break;

                            case "info:created_at":
                                item.CreatedAt =
                                    value;
                                break;

                            case "info:status":
                                item.Status =
                                    value;
                                break;

                            case "info:approved_time":
                                item.ApprovedTime =
                                    value;
                                break;

                            case "info:table_id":
                                item.TableId =
                                    value;
                                break;

                            case "info:cadaver_row_key":
                                item.CadaverRowKey =
                                    value;
                                break;

                            case "info:admin_reply":
                                item.AdminReply =
                                    value;
                                break;
                        }
                    }

                    result.Add(item);
                }

                return result
                    .OrderByDescending(x =>
                        DateTime.TryParse(
                            x.CreatedAt,
                            out DateTime date
                        )
                            ? date
                            : DateTime.MinValue
                    )
                    .ToList();
            }
            catch
            {
                return result;
            }
        }
        [HttpGet]
        public async Task<IActionResult> TeachingApplication(
            string rowKey = "")
        {
            var block =
                RequireLogin();

            if (block != null)
                return block;

            string role =
                HttpContext.Session
                    .GetString("Role")
                ?? "";

            if (role != "Teacher" &&
                role != "Admin")
            {
                return Forbid();
            }

            rowKey =
                rowKey?.Trim() ?? "";

            // =========================
            // 老師：建立新申請
            // =========================
            if (role == "Teacher" &&
                string.IsNullOrWhiteSpace(rowKey))
            {
                ViewBag.CanSubmit = true;

                return View(
                    new TeachingApplication()
                );
            }

            // =========================
            // Admin：不可建立空白申請
            // =========================
            if (role == "Admin" &&
                string.IsNullOrWhiteSpace(rowKey))
            {
                return RedirectToAction(
                    "TeachingApplicationReport"
                );
            }

            // =========================
            // 查看指定申請
            // =========================
            var applications =
                await GetTeachingApplications();
var application =
    applications.FirstOrDefault(x =>
        string.Equals(
            x.RowKey,
            rowKey,
            StringComparison.OrdinalIgnoreCase
        )
    );

            if (application == null)
            {
                TempData["Message"] =
                    "找不到指定的教學申請。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "TeachingApplicationReport"
                );
            }

            ViewBag.CanSubmit = false;

            return View(application);
        }
        [HttpGet]
        public async Task<IActionResult> TeachingApplicationReport()
        {
            var block =
                RequireLogin();

            if (block != null)
                return block;

            string role =
                HttpContext.Session
                    .GetString("Role")
                ?? "";

            if (role != "Teacher" &&
                role != "Student" &&
                role != "Admin")
            {
                return Forbid();
            }

            var applications =
                await GetTeachingApplications();

            return View(applications);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTeachingApplication(
    string rowKey)
        {
            var block =
                RequireLogin();

            if (block != null)
                return block;

            string role =
                HttpContext.Session
                    .GetString("Role")
                ?? "";

            string loginId =
                HttpContext.Session
                    .GetString("LoginId")
                ?? "";

            // =========================
            // 只有 Teacher / Admin 可以刪除
            // =========================
            if (role != "Teacher" &&
                role != "Admin")
            {
                return Forbid();
            }

            rowKey =
                rowKey?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(rowKey))
            {
                TempData["Message"] =
                    "刪除失敗：缺少申請編號。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "TeachingApplicationReport"
                );
            }

            // =========================
            // 取得目前申請
            // =========================
            var applications =
                await GetTeachingApplications();

            var application =
                applications.FirstOrDefault(x =>
                    string.Equals(
                        x.RowKey,
                        rowKey,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (application == null)
            {
                TempData["Message"] =
                    "找不到指定的教學申請。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "TeachingApplicationReport"
                );
            }

            // =========================
            // Teacher：
            // 只能刪除自己的 Pending 申請
            // =========================
            if (role == "Teacher")
            {
                if (!string.Equals(
                    application.Applicant?.Trim(),
                    loginId.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return Forbid();
                }

                if (!string.Equals(
                    application.Status,
                    "Pending",
                    StringComparison.OrdinalIgnoreCase))
                {
                    TempData["Message"] =
                        "此申請已完成審核，無法刪除。";

                    TempData["MessageType"] =
                        "error";

                    return RedirectToAction(
                        "TeachingApplicationReport"
                    );
                }
            }

            // =========================
            // Admin：
            // 可刪除任何人的任何狀態申請
            // =========================

            try
            {
                string encodedRowKey =
                    Uri.EscapeDataString(
                        rowKey
                    );

                var response =
                    await _client.DeleteAsync(
                        $"teaching_application/{encodedRowKey}"
                    );

                if (!response.IsSuccessStatusCode &&
                    response.StatusCode !=
                        System.Net.HttpStatusCode.NotFound)
                {
                    TempData["Message"] =
                        "刪除申請失敗。";

                    TempData["MessageType"] =
                        "error";

                    return RedirectToAction(
                        "TeachingApplicationReport"
                    );
                }

                TempData["Message"] =
                    "申請已刪除。";

                TempData["MessageType"] =
                    "success";
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "刪除教學申請失敗。RowKey={RowKey}",
                    rowKey
                );

                TempData["Message"] =
                    "刪除申請時發生錯誤。";

                TempData["MessageType"] =
                    "error";
            }

            return RedirectToAction(
                "TeachingApplicationReport"
            );
        }
        // =========================
        // 學生課程筆記
        // =========================
        private async Task<List<StudentDailyNote>> GetStudentDailyNotes(
            string studentId)
        {
            var result = new List<StudentDailyNote>();

            studentId = studentId?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(studentId))
                return result;

            try
            {
                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(10)
                    );

                var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        "student_course_note/*"
                    );

                request.Headers.Add(
                    "Accept",
                    "application/json"
                );

                var response =
                    await _client.SendAsync(
                        request,
                        cts.Token
                    );

                if (!response.IsSuccessStatusCode)
                    return result;

                string json =
                    await response.Content
                        .ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(json))
                    return result;

                using JsonDocument doc =
                    JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty(
                        "Row",
                        out JsonElement rows))
                {
                    return result;
                }

                foreach (var row in rows.EnumerateArray())
                {
                    string rowKey =
                        Decode(
                            row.GetProperty("key")
                                .GetString() ?? ""
                        );

                    var item =
                        new StudentDailyNote
                        {
                            RowKey = rowKey
                        };

                    if (!row.TryGetProperty(
                            "Cell",
                            out JsonElement cells))
                    {
                        continue;
                    }

                    foreach (var cell in cells.EnumerateArray())
                    {
                        string column =
                            Decode(
                                cell.GetProperty("column")
                                    .GetString() ?? ""
                            );

                        string value =
                            Decode(
                                cell.GetProperty("$")
                                    .GetString() ?? ""
                            );

                        if (column == "note:student_id")
                            item.StudentId = value;
                        else if (column == "note:note_date")
                            item.NoteDate = value;
                        else if (column == "note:content")
                            item.Content = value;
                        else if (column == "note:updated_at")
                            item.UpdatedAt = value;
                    }

                    // 舊課程筆記沒有 note:note_date，不會混進課程筆記。
                    if (!string.Equals(
                            item.StudentId,
                            studentId,
                            StringComparison.OrdinalIgnoreCase)
                        ||
                        string.IsNullOrWhiteSpace(item.NoteDate))
                    {
                        continue;
                    }

                    result.Add(item);
                }

                return result
                    .OrderByDescending(x =>
                        DateTime.TryParse(
                            x.NoteDate,
                            out DateTime d)
                            ? d
                            : DateTime.MinValue
                    )
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "讀取課程筆記失敗 StudentId={StudentId}",
                    studentId
                );

                return result;
            }
        }

        private async Task PutStudentDailyNoteRow(
            string rowKey,
            string studentId,
            string noteDate,
            string content)
        {
            string updatedAt =
                DateTime.Now.ToString(
                    "yyyy-MM-ddTHH:mm:ss"
                );

            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(rowKey)}"">
    <Cell column=""{ToBase64("note:student_id")}"">{ToBase64(studentId)}</Cell>
    <Cell column=""{ToBase64("note:note_date")}"">{ToBase64(noteDate)}</Cell>
    <Cell column=""{ToBase64("note:content")}"">{ToBase64(content)}</Cell>
    <Cell column=""{ToBase64("note:updated_at")}"">{ToBase64(updatedAt)}</Cell>
  </Row>
</CellSet>";

            var response =
                await _client.PutAsync(
                    $"student_course_note/{Uri.EscapeDataString(rowKey)}",
                    new StringContent(
                        xml,
                        Encoding.UTF8,
                        "text/xml"
                    )
                );

            response.EnsureSuccessStatusCode();
        }

        private async Task PutClassScheduleRow(
    string rowKey,
    string classSessionId,
    string classDate,
    string tableId,
    string cadaverRowKey)
        {
            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(rowKey)}"">

    <Cell column=""{ToBase64("schedule:class_session_id")}"">
        {ToBase64(classSessionId)}
    </Cell>

    <Cell column=""{ToBase64("schedule:class_date")}"">
        {ToBase64(classDate)}
    </Cell>

    <Cell column=""{ToBase64("schedule:table_id")}"">
        {ToBase64(tableId)}
    </Cell>

    <Cell column=""{ToBase64("schedule:cadaver_row_key")}"">
        {ToBase64(cadaverRowKey)}
    </Cell>

  </Row>
</CellSet>";

            var content =
                new StringContent(
                    xml,
                    Encoding.UTF8,
                    "text/xml"
                );

            var response =
                await _client.PutAsync(
                    $"class_schedule/{rowKey}",
                    content
                );

            response.EnsureSuccessStatusCode();
        }
        private async Task<List<SurgicalTable>> GetSurgicalTables()
        {
            var result = new List<SurgicalTable>();

            try
            {
                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(10)
                    );

                var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        "surgical_table/*"
                    );

                request.Headers.Add(
                    "Accept",
                    "application/json"
                );

                var response =
                    await _client.SendAsync(
                        request,
                        cts.Token
                    );

                if (!response.IsSuccessStatusCode)
                    return result;

                string json =
                    await response.Content
                        .ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(json))
                    return result;

                using JsonDocument doc =
                    JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty(
                        "Row",
                        out JsonElement rows))
                {
                    return result;
                }

                foreach (var row in rows.EnumerateArray())
                {
                    string rowKey =
                        Decode(
                            row.GetProperty("key")
                                .GetString() ?? ""
                        );

                    var item =
                        new SurgicalTable
                        {
                            RowKey = rowKey
                        };

                    if (!row.TryGetProperty(
                            "Cell",
                            out JsonElement cells))
                    {
                        result.Add(item);
                        continue;
                    }

                    foreach (
                        var cell
                        in cells.EnumerateArray())
                    {
                        string column =
                            Decode(
                                cell.GetProperty("column")
                                    .GetString() ?? ""
                            );

                        string value =
                            Decode(
                                cell.GetProperty("$")
                                    .GetString() ?? ""
                            );

                        if (column == "table:number")
                        {
                            item.TableNumber = value;
                        }
                        else if (column == "table:name")
                        {
                            item.TableName = value;
                        }
                        else if (column == "table:active")
                        {
                            item.IsActive =
                                value == "1" ||
                                value.Equals(
                                    "true",
                                    StringComparison.OrdinalIgnoreCase
                                );
                        }
                    }

                    result.Add(item);
                }

                return result
                    .OrderBy(x => x.TableNumber)
                    .ToList();
            }
            catch
            {
                return result;
            }
        }
        private async Task PutSurgicalTableRow(
    string rowKey,
    string tableNumber,
    string tableName,
    bool isActive)
        {
            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(rowKey)}"">

    <Cell column=""{ToBase64("table:number")}"">
        {ToBase64(tableNumber)}
    </Cell>

    <Cell column=""{ToBase64("table:name")}"">
        {ToBase64(tableName)}
    </Cell>

    <Cell column=""{ToBase64("table:active")}"">
        {ToBase64(isActive ? "1" : "0")}
    </Cell>

  </Row>
</CellSet>";

            var content =
                new StringContent(
                    xml,
                    Encoding.UTF8,
                    "text/xml"
                );

            var response =
                await _client.PutAsync(
                    $"surgical_table/{rowKey}",
                    content
                );

            response.EnsureSuccessStatusCode();
        }
        private async Task PutPatientRow(
            string row,
            string medicalRecordNumber,
            string gender,
            string bloodType,
            string bloodTypeSource,
            string drugAllergyHistory,
            string pastMedicalHistory,
            string dissectionStatus,
            string dataSource,
            string dataVersion)
        {
            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">
    <Cell column=""{ToBase64("patient:id")}"">
        {ToBase64(row)}
    </Cell>

    <Cell column=""{ToBase64("patient:medical_record_number")}"">
        {ToBase64(medicalRecordNumber)}
    </Cell>

    <Cell column=""{ToBase64("patient:gender")}"">
        {ToBase64(gender)}
    </Cell>

    <Cell column=""{ToBase64("patient:blood_type")}"">
        {ToBase64(bloodType)}
    </Cell>

    <Cell column=""{ToBase64("patient:blood_type_source")}"">
        {ToBase64(bloodTypeSource)}
    </Cell>

    <Cell column=""{ToBase64("patient:drug_allergy_history")}"">
        {ToBase64(drugAllergyHistory)}
    </Cell>

    <Cell column=""{ToBase64("patient:past_medical_history")}"">
        {ToBase64(pastMedicalHistory)}
    </Cell>
<Cell column=""{ToBase64("patient:dissection_status")}"">
    {ToBase64(dissectionStatus)}
</Cell>

    <Cell column=""{ToBase64("patient:data_source")}"">
        {ToBase64(dataSource)}
    </Cell>

    <Cell column=""{ToBase64("patient:data_version")}"">
        {ToBase64(dataVersion)}
    </Cell>
  </Row>
</CellSet>";

            var response = await _client.PutAsync(
                $"cadaver/{row}",
                new StringContent(
                    xml,
                    Encoding.UTF8,
                    "text/xml"
                )
            );

            response.EnsureSuccessStatusCode();
        }
        private async Task PutTeachingRow(
    string row,
    string dissectionProcessRecord,
    string teacherExplanationRecord,
    string teachingNote,
    string pathologicalFindings,
    string relatedDiagnosisCode,
    string relatedTestItemCode,
    string relatedOrderCode)
        {
            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">
    <Cell column=""{ToBase64("teaching:dissection_process_record")}"">{ToBase64(dissectionProcessRecord)}</Cell>
    <Cell column=""{ToBase64("teaching:teacher_explanation_record")}"">{ToBase64(teacherExplanationRecord)}</Cell>
    <Cell column=""{ToBase64("teaching:teaching_note")}"">{ToBase64(teachingNote)}</Cell>
    <Cell column=""{ToBase64("teaching:pathological_findings")}"">{ToBase64(pathologicalFindings)}</Cell>
    <Cell column=""{ToBase64("teaching:related_diagnosis_code")}"">{ToBase64(relatedDiagnosisCode)}</Cell>
    <Cell column=""{ToBase64("teaching:related_test_item_code")}"">{ToBase64(relatedTestItemCode)}</Cell>
    <Cell column=""{ToBase64("teaching:related_order_code")}"">{ToBase64(relatedOrderCode)}</Cell>
  </Row>
</CellSet>";

            var content = new StringContent(xml, Encoding.UTF8, "text/xml");
            await _client.PutAsync($"cadaver/{row}", content);
        }
        private async Task PutDissectionDates(
    string row,
    string donationDate,
    string dissectionDate)
        {
            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">
    <Cell column=""{ToBase64("patient:donation_date")}"">
        {ToBase64(donationDate)}
    </Cell>
    <Cell column=""{ToBase64("patient:dissection_date")}"">
        {ToBase64(dissectionDate)}
    </Cell>
  </Row>
</CellSet>";

            var content = new StringContent(
                xml,
                Encoding.UTF8,
                "text/xml"
            );

            var response = await _client.PutAsync(
                $"cadaver/{row}",
                content
            );

            response.EnsureSuccessStatusCode();
        }
        private async Task PutLabTestRow(
            string row,
            string recordId,
            string labTestDate,
            string labTestItem,
            string labTestResult,
            string labTestItemCode,
            string labTestItemName,
            string labResultType,
            string labNumericResult,
            string labTextResult,
            string labImageResultPath,
            string labWaveformResultPath,
            string labAudioResultPath,

            string labExaminationType,
            string labBodyPart,
            string labInterpretation,
            string labDicomResultPath,
            string labVideoResultPath,
            string labReportFilePath,

            string labUnit,
            string labReferenceRange,
            string labAbnormalFlag,
            string labTestMethod,
            string labSpecimenCollectedAt)
        {
            string prefix = $"lab_test:record_{recordId}_";

            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">
    <Cell column=""{ToBase64(prefix + "test_date")}"">
        {ToBase64(labTestDate)}
    </Cell>

    <Cell column=""{ToBase64(prefix + "test_item")}"">
        {ToBase64(labTestItem)}
    </Cell>

    <Cell column=""{ToBase64(prefix + "test_result")}"">
        {ToBase64(labTestResult)}
    </Cell>

    <Cell column=""{ToBase64(prefix + "test_item_code")}"">
        {ToBase64(labTestItemCode)}
    </Cell>

    <Cell column=""{ToBase64(prefix + "test_item_name")}"">
        {ToBase64(labTestItemName)}
    </Cell>

    <Cell column=""{ToBase64(prefix + "result_type")}"">
        {ToBase64(labResultType)}
    </Cell>

    <Cell column=""{ToBase64(prefix + "numeric_result")}"">
        {ToBase64(labNumericResult)}
    </Cell>

    <Cell column=""{ToBase64(prefix + "text_result")}"">
        {ToBase64(labTextResult)}
    </Cell>

    <Cell column=""{ToBase64(prefix + "image_result_path")}"">
        {ToBase64(labImageResultPath)}
    </Cell>

    <Cell column=""{ToBase64(prefix + "waveform_result_path")}"">
        {ToBase64(labWaveformResultPath)}
    </Cell>

    <Cell column=""{ToBase64(prefix + "audio_result_path")}"">
        {ToBase64(labAudioResultPath)}
    </Cell>
<Cell column=""{ToBase64(prefix + "examination_type")}"">
    {ToBase64(labExaminationType)}
</Cell>

<Cell column=""{ToBase64(prefix + "body_part")}"">
    {ToBase64(labBodyPart)}
</Cell>

<Cell column=""{ToBase64(prefix + "interpretation")}"">
    {ToBase64(labInterpretation)}
</Cell>

<Cell column=""{ToBase64(prefix + "dicom_result_path")}"">
    {ToBase64(labDicomResultPath)}
</Cell>

<Cell column=""{ToBase64(prefix + "video_result_path")}"">
    {ToBase64(labVideoResultPath)}
</Cell>

<Cell column=""{ToBase64(prefix + "report_file_path")}"">
    {ToBase64(labReportFilePath)}
</Cell>   
<Cell column=""{ToBase64(prefix + "unit")}"">
        {ToBase64(labUnit)}
    </Cell>

    <Cell column=""{ToBase64(prefix + "reference_range")}"">
        {ToBase64(labReferenceRange)}
    </Cell>

    <Cell column=""{ToBase64(prefix + "abnormal_flag")}"">
        {ToBase64(labAbnormalFlag)}
    </Cell>

    <Cell column=""{ToBase64(prefix + "test_method")}"">
        {ToBase64(labTestMethod)}
    </Cell>

    <Cell column=""{ToBase64(prefix + "specimen_collected_at")}"">
        {ToBase64(labSpecimenCollectedAt)}
    </Cell>
  </Row>
</CellSet>";

            var content = new StringContent(
                xml,
                Encoding.UTF8,
                "text/xml"
            );

            var response = await _client.PutAsync(
                $"cadaver/{row}",
                content
            );

            response.EnsureSuccessStatusCode();
        }
        private async Task PutAdmissionRow(
    string row,
    string admissionStartDate,
    string admissionEndDate,
    string wardType,
    string admissionReason,
    string admissionReasonCode,
    string admissionDiagnosisCode,
    string dischargeDiagnosisCode,
    string primaryDiagnosisCode,
    string secondaryDiagnosisCode,
    string hospitalCode,
    string departmentCode,
    string dischargeStatus,
    string lengthOfStay,
    string icuDays,
    string inpatientOrderCode,
    string procedureCode)
        {
            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">
    <Cell column=""{ToBase64("admission:start_date")}"">{ToBase64(admissionStartDate)}</Cell>
    <Cell column=""{ToBase64("admission:end_date")}"">{ToBase64(admissionEndDate)}</Cell>
    <Cell column=""{ToBase64("admission:ward_type")}"">{ToBase64(wardType)}</Cell>
    <Cell column=""{ToBase64("admission:admission_reason")}"">{ToBase64(admissionReason)}</Cell>
    <Cell column=""{ToBase64("admission:admission_reason_code")}"">{ToBase64(admissionReasonCode)}</Cell>
    <Cell column=""{ToBase64("admission:admission_diagnosis_code")}"">{ToBase64(admissionDiagnosisCode)}</Cell>
    <Cell column=""{ToBase64("admission:discharge_diagnosis_code")}"">{ToBase64(dischargeDiagnosisCode)}</Cell>
    <Cell column=""{ToBase64("admission:primary_diagnosis_code")}"">{ToBase64(primaryDiagnosisCode)}</Cell>
    <Cell column=""{ToBase64("admission:secondary_diagnosis_code")}"">{ToBase64(secondaryDiagnosisCode)}</Cell>
    <Cell column=""{ToBase64("admission:hospital_code")}"">{ToBase64(hospitalCode)}</Cell>
    <Cell column=""{ToBase64("admission:department_code")}"">{ToBase64(departmentCode)}</Cell>
    <Cell column=""{ToBase64("admission:discharge_status")}"">{ToBase64(dischargeStatus)}</Cell>
    <Cell column=""{ToBase64("admission:length_of_stay")}"">{ToBase64(lengthOfStay)}</Cell>
    <Cell column=""{ToBase64("admission:icu_days")}"">{ToBase64(icuDays)}</Cell>
    <Cell column=""{ToBase64("admission:inpatient_order_code")}"">{ToBase64(inpatientOrderCode)}</Cell>
    <Cell column=""{ToBase64("admission:procedure_code")}"">{ToBase64(procedureCode)}</Cell>
  </Row>
</CellSet>";

            var content = new StringContent(xml, Encoding.UTF8, "text/xml");
            await _client.PutAsync($"cadaver/{row}", content);
        }

        // 寫入基本資料

        private async Task PutRow(string row, string gender, string age, string donationYear, string dissectionDate)
        {
            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">
    <Cell column=""{ToBase64("info:gender")}"">{ToBase64(gender)}</Cell>
    <Cell column=""{ToBase64("info:age")}"">{ToBase64(age)}</Cell>
    <Cell column=""{ToBase64("info:donation_year")}"">{ToBase64(donationYear)}</Cell>
    <Cell column=""{ToBase64("info:dissection_date")}"">{ToBase64(dissectionDate)}</Cell>
  </Row>
</CellSet>";

            var content = new StringContent(xml, Encoding.UTF8, "text/xml");
            await _client.PutAsync($"cadaver/{row}", content);
        }

        // =========================
        // 寫入媒體（圖片 / 影片）
        // =========================
        private async Task SaveMedia(string row, string type, string path, string note, bool isAlive)
        {
            string id = Guid.NewGuid().ToString("N");

            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">
    <Cell column=""{ToBase64($"info:{type}_{id}")}"">{ToBase64(path)}</Cell>
    <Cell column=""{ToBase64($"info:note_{id}")}"">{ToBase64(note)}</Cell>
    <Cell column=""{ToBase64($"info:isAlive_{id}")}"">{ToBase64(isAlive ? "1" : "0")}</Cell>
  </Row>
</CellSet>";

            var content = new StringContent(xml, Encoding.UTF8, "text/xml");
            await _client.PutAsync($"cadaver/{row}", content);
        }
        private async Task<string> SaveImportedMedicalImageToHBase(
            string row,
            string sourceFile,
            string examinationType,
            string symptomCategory,
            string source,
            string note)
        {
            row = row?.Trim() ?? "";
            examinationType = examinationType?.Trim() ?? "";
            symptomCategory = symptomCategory?.Trim() ?? "";
            source = source?.Trim() ?? "";
            note = note?.Trim() ?? "";
            
            if (string.IsNullOrWhiteSpace(row))
            {
                throw new InvalidOperationException(
                    "缺少病人 RowKey。"
                );
            }

            if (!System.IO.File.Exists(sourceFile))
            {
                throw new FileNotFoundException(
                    $"找不到圖片：{sourceFile}"
                );
            }

            string extension =
                Path.GetExtension(sourceFile)
                    .ToLowerInvariant();

            string contentType =
                extension switch
                {
                    ".jpg" => "image/jpeg",
                    ".jpeg" => "image/jpeg",
                    ".png" => "image/png",
                    _ => throw new InvalidOperationException(
                        $"不支援的圖片格式：{extension}"
                    )
                };

            byte[] imageBytes =
                await System.IO.File.ReadAllBytesAsync(
                    sourceFile
                );

            if (imageBytes.Length == 0)
            {
                throw new InvalidOperationException(
                    $"圖片內容為空：{sourceFile}"
                );
            }

            string imageId =
                Guid.NewGuid().ToString("N");

            string blobRowKey =
                $"{row}_{imageId}";

            string imageBase64 =
                Convert.ToBase64String(
                    imageBytes
                );

            // =========================================
            // 1. 圖片本體 → medical_image_blob
            // =========================================

            string blobXml = $@"
<CellSet>
  <Row key=""{ToBase64(blobRowKey)}"">
    <Cell column=""{ToBase64("data:binary")}"">
        {imageBase64}
    </Cell>
  </Row>
</CellSet>";

            using (
                HttpResponseMessage blobResponse =
                    await _client.PutAsync(
                        $"medical_image_blob/{Uri.EscapeDataString(blobRowKey)}",
                        new StringContent(
                            blobXml,
                            Encoding.UTF8,
                            "text/xml"
                        )
                    )
            )
            {
                blobResponse.EnsureSuccessStatusCode();
            }

            string imageUrl =
                $"/Home/HBaseMedicalImage" +
                $"?row={Uri.EscapeDataString(row)}" +
                $"&imageId={Uri.EscapeDataString(imageId)}";

            string fileName =
                Path.GetFileName(sourceFile);

            // =========================================
            // 2. metadata → cadaver:medical_image
            // =========================================

            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">

    <Cell column=""{ToBase64($"medical_image:image_{imageId}")}"">
        {ToBase64(imageUrl)}
    </Cell>

    <Cell column=""{ToBase64($"medical_image:content_type_{imageId}")}"">
        {ToBase64(contentType)}
    </Cell>

    <Cell column=""{ToBase64($"medical_image:file_name_{imageId}")}"">
        {ToBase64(fileName)}
    </Cell>

    <Cell column=""{ToBase64($"medical_image:note_{imageId}")}"">
        {ToBase64(note)}
    </Cell>

    <Cell column=""{ToBase64($"medical_image:examination_type_{imageId}")}"">
        {ToBase64(examinationType)}
    </Cell>

    <Cell column=""{ToBase64($"medical_image:symptom_category_{imageId}")}"">
        {ToBase64(symptomCategory)}
    </Cell>

    <Cell column=""{ToBase64($"medical_image:source_{imageId}")}"">
        {ToBase64(source)}
    </Cell>

  </Row>
</CellSet>";

            using (
                HttpResponseMessage response =
                    await _client.PutAsync(
                        $"cadaver/{Uri.EscapeDataString(row)}",
                        new StringContent(
                            xml,
                            Encoding.UTF8,
                            "text/xml"
                        )
                    )
            )
            {
                response.EnsureSuccessStatusCode();
            }

            return imageId;
        }
        private static string GetImportedExaminationType(
    string imagePath)
        {
            string folder =
                Directory.GetParent(imagePath)?.Name
                ?.Trim()
                ?? "";

            return folder.ToLowerInvariant() switch
            {
                "xray" => "XRay",
                "ct" => "CT",
                "mri" => "MRI",
                "ultrasound" => "Ultrasound",
                "pathology" => "Pathology",
                "endoscopy" => "Endoscopy",
                "pet" => "PET",
                "mammography" => "Mammography",
                "angiography" => "Angiography",
                "microscopy" => "Microscopy",
                _ => "Other"
            };
        }
        private async Task SaveCategorizedImage(
    string row,
    string imageType,
    string path,
    string note,
    string examinationType,
    string symptomCategory)
        {
            string id =
                Guid.NewGuid().ToString("N");

            string family =
        imageType == "course"
        ? "course_image"
        : "medical_image";

            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">

    <Cell column=""{ToBase64($"{family}:image_{id}")}"">
        {ToBase64(path)}
    </Cell>

    <Cell column=""{ToBase64($"{family}:note_{id}")}"">
        {ToBase64(note)}
    </Cell>

    <Cell column=""{ToBase64($"{family}:examination_type_{id}")}"">
        {ToBase64(examinationType)}
    </Cell>

    <Cell column=""{ToBase64($"{family}:symptom_category_{id}")}"">
        {ToBase64(symptomCategory)}
    </Cell>

  </Row>
</CellSet>";

            var content =
                new StringContent(
                    xml,
                    Encoding.UTF8,
                    "text/xml"
                );

            var response =
                await _client.PutAsync(
                    $"cadaver/{row}",
                    content
                );

            response.EnsureSuccessStatusCode();
        }

        [HttpGet]
        public async Task<IActionResult> HBaseMedicalImage(
    string row,
    string imageId)
        {
            var block = RequireLogin();

            if (block != null)
                return block;

            row = row?.Trim() ?? "";
            imageId = imageId?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(row) ||
                string.IsNullOrWhiteSpace(imageId))
            {
                return BadRequest(
                    "缺少 row 或 imageId。"
                );
            }

            try
            {
                // ============================================
                // 1. 從 medical_image_blob 讀取圖片 binary
                // ============================================

                string blobRowKey =
                    $"{row}_{imageId}";

                string dataPath =
                    $"medical_image_blob/" +
                    $"{Uri.EscapeDataString(blobRowKey)}" +
                    "/data:binary";

                using var dataRequest =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        dataPath
                    );

                dataRequest.Headers
                    .TryAddWithoutValidation(
                        "Accept",
                        "text/xml"
                    );

                using HttpResponseMessage dataResponse =
                    await _client.SendAsync(
                        dataRequest
                    );

                if (!dataResponse.IsSuccessStatusCode)
                {
                    string errorBody =
                        await dataResponse.Content
                            .ReadAsStringAsync();

                    _logger.LogError(
                        "讀取 HBase 圖片 binary 失敗。" +
                        " Row={Row}, ImageId={ImageId}," +
                        " Status={Status}, Body={Body}",
                        row,
                        imageId,
                        dataResponse.StatusCode,
                        errorBody
                    );

                    return NotFound(
                        "找不到圖片資料。"
                    );
                }

                string dataXml =
                    await dataResponse.Content
                        .ReadAsStringAsync();

                byte[]? imageBytes =
                    ExtractHBaseCellBytes(
                        dataXml
                    );

                if (imageBytes == null ||
                    imageBytes.Length == 0)
                {
                    return NotFound(
                        "圖片資料為空。"
                    );
                }

                // ==================================================
                // 相容兩種 HBase 圖片格式
                //
                // 1. 網站原本上傳：HBase 內直接是原始圖片 bytes
                // 2. Ruby 批次匯入：HBase 內是 Base64 文字
                // ==================================================

                bool isJpeg =
                    imageBytes.Length >= 3 &&
                    imageBytes[0] == 0xFF &&
                    imageBytes[1] == 0xD8 &&
                    imageBytes[2] == 0xFF;

                bool isPng =
                    imageBytes.Length >= 8 &&
                    imageBytes[0] == 0x89 &&
                    imageBytes[1] == 0x50 &&
                    imageBytes[2] == 0x4E &&
                    imageBytes[3] == 0x47 &&
                    imageBytes[4] == 0x0D &&
                    imageBytes[5] == 0x0A &&
                    imageBytes[6] == 0x1A &&
                    imageBytes[7] == 0x0A;

                if (!isJpeg && !isPng)
                {
                    try
                    {
                        string base64Text =
                            Encoding.UTF8.GetString(
                                imageBytes
                            ).Trim();

                        imageBytes =
                            Convert.FromBase64String(
                                base64Text
                            );
                    }
                    catch (FormatException)
                    {
                        return StatusCode(
                            500,
                            "HBase 圖片不是有效的 JPEG/PNG 或 Base64 圖片資料。"
                        );
                    }
                }

                // ============================================
                // 2. MIME type 仍然從原本 cadaver 讀
                // ============================================

                string contentType =
                    "image/jpeg";

                string typePath =
                    $"cadaver/{Uri.EscapeDataString(row)}" +
                    $"/medical_image:content_type_{Uri.EscapeDataString(imageId)}";

                using var typeRequest =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        typePath
                    );

                typeRequest.Headers
                    .TryAddWithoutValidation(
                        "Accept",
                        "text/xml"
                    );

                using HttpResponseMessage typeResponse =
                    await _client.SendAsync(
                        typeRequest
                    );

                if (typeResponse.IsSuccessStatusCode)
                {
                    string typeXml =
                        await typeResponse.Content
                            .ReadAsStringAsync();

                    byte[]? typeBytes =
                        ExtractHBaseCellBytes(
                            typeXml
                        );

                    if (typeBytes != null &&
                        typeBytes.Length > 0)
                    {
                        string storedContentType =
                            Encoding.UTF8.GetString(
                                typeBytes
                            );

                        if (!string.IsNullOrWhiteSpace(
                                storedContentType))
                        {
                            contentType =
                                storedContentType.Trim();
                        }
                    }
                }

                return File(
                    imageBytes,
                    contentType
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "從 HBase 讀取圖片失敗。" +
                    " Row={Row}, ImageId={ImageId}",
                    row,
                    imageId
                );

                return StatusCode(
                    500,
                    $"讀取 HBase 圖片失敗：{ex.Message}"
                );
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportNewDemo30()
        {
            var block = RequireTeacher();

            if (block != null)
                return block;

            try
            {
                string importFolder =
                    Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "wwwroot",
                        "import30"
                    );

                if (!Directory.Exists(importFolder))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error =
                            $"找不到批次影像資料夾：{importFolder}"
                    });
                }

                string[] imageFiles =
                    Directory.GetFiles(
                        importFolder,
                        "*.*",
                        SearchOption.AllDirectories
                    )
                    .Where(path =>
                    {
                        string ext =
                            Path.GetExtension(path)
                                .ToLowerInvariant();

                        return ext == ".jpg"
                            || ext == ".jpeg"
                            || ext == ".png";
                    })
                    .OrderBy(path => path)
                    .ToArray();

                if (imageFiles.Length == 0)
                {
                    return BadRequest(new
                    {
                        success = false,
                        error =
                            "import30 資料夾內沒有 JPG、JPEG 或 PNG 圖片。"
                    });
                }

                const int patientCount = 30;

                string[] genders =
                {
            "男",
            "女"
        };

                string[] bloodTypes =
                {
            "A",
            "B",
            "O",
            "AB"
        };

                string[] pastHistories =
                {
            "高血壓",
            "第二型糖尿病",
            "高血脂",
            "氣喘",
            "慢性腎臟病",
            "冠狀動脈疾病",
            "無特殊病史"
        };

                string[] diagnoses =
                {
            "肺部疾病",
            "肝臟疾病",
            "腦部疾病",
            "心血管疾病",
            "腎臟疾病",
            "消化系統疾病",
            "腫瘤相關疾病"
        };

                Random random = new Random();

                // ================================
                // 先建立 30 位病人
                // ================================

                for (int i = 0; i < patientCount; i++)
                {
                    int number = i + 1;

                    string row =
                        $"NEW{number:D3}";

                    string medicalRecordNumber =
                        $"MR-NEW-{number:D4}";

                    string gender =
                        genders[i % genders.Length];

                    string bloodType =
                        bloodTypes[i % bloodTypes.Length];

                    string history =
                        pastHistories[
                            i % pastHistories.Length
                        ];

                    string diagnosis =
                        diagnoses[
                            i % diagnoses.Length
                        ];

                    int age =
                        random.Next(
                            20,
                            90
                        );

                    // ----------------------------
                    // patient:*
                    // ----------------------------

                    await PutPatientRow(
                        row,
                        medicalRecordNumber,
                        gender,
                        bloodType,
                        "模擬資料",
                        "無已知藥物過敏",
                        history,
                        "0",
                        "demo_import_20260917",
                        "2026-09-demo-v1"
                    );

                    // 你的 Age 目前不是 PutPatientRow 的參數，
                    // 因此用既有 PutRow 補 info:age。
                    await PutRow(
                        row,
                        gender,
                        age.ToString(),
                        "",
                        ""
                    );

                    // ----------------------------
                    // teaching:*
                    // ----------------------------

                    await PutTeachingRow(
                        row,
                        "",
                        "",
                        $"教學示範病例：{diagnosis}",
                        $"{diagnosis}相關教學案例。",
                        diagnosis,
                        "",
                        ""
                    );

                    // ----------------------------
                    // admission:*
                    // ----------------------------

                    DateTime admissionDate =
                        new DateTime(
                            2026,
                            1,
                            1
                        )
                        .AddDays(
                            i * 6
                        );

                    DateTime dischargeDate =
                        admissionDate
                            .AddDays(
                                random.Next(
                                    3,
                                    11
                                )
                            );

                    await PutAdmissionRow(
                        row,
                        admissionDate
                            .ToString("yyyy-MM-dd"),
                        dischargeDate
                            .ToString("yyyy-MM-dd"),
                        "一般病房",
                        $"{diagnosis}相關住院檢查",
                        $"ADM-{number:D4}",
                        diagnosis,
                        diagnosis,
                        diagnosis,
                        "",
                        "DEMO-HOSP",
                        "教學醫院",
                        "病況穩定出院",
                        (
                            dischargeDate
                            -
                            admissionDate
                        )
                        .Days
                        .ToString(),
                        "0",
                        $"ORD-{number:D4}",
                        ""
                    );

                    // ----------------------------
                    // FHIR-like Patient
                    // ----------------------------

                    await SavePatientSnapshot(
                        row,
                        gender,
                        age.ToString(),
                        "",
                        ""
                    );
                }

                // ================================
                // 再分配所有圖片
                //
                // 不限制 30 張：
                // 30 人 + 45 張也可以
                // ================================

                int importedImageCount = 0;

                for (int i = 0;
                     i < imageFiles.Length;
                     i++)
                {
                    string imageFile =
                        imageFiles[i];

                    // round-robin：
                    // 第 1 張 → NEW001
                    // ...
                    // 第 30 張 → NEW030
                    // 第 31 張 → NEW001
                    int patientNumber =
                        (i % patientCount) + 1;

                    string row =
                        $"NEW{patientNumber:D3}";

                    string examinationType =
                        GetImportedExaminationType(
                            imageFile
                        );

                    string symptomCategory =
                        GetImportedSymptomCategory(
                            examinationType
                        );

                    string source =
                        "公開教學影像資料集";

                    string note =
                        "教學參考影像（非病人真實臨床影像）";

                    await SaveImportedMedicalImageToHBase(
                        row,
                        imageFile,
                        examinationType,
                        symptomCategory,
                        source,
                        note
                    );

                    // 同時建立一筆 lab_test，
                    // 讓 AI / 病歷查詢可以知道這張影像屬於什麼檢查。

                    string labRecordId =
                        Guid.NewGuid()
                            .ToString("N");

                    string bodyPart =
                        GetImportedBodyPart(
                            examinationType
                        );

                    await PutLabTestRow(
                        row,
                        labRecordId,
                        DateTime.Now
                            .AddDays(-i)
                            .ToString("yyyy-MM-dd"),

                        examinationType,

                        $"{examinationType} 教學影像",

                        examinationType,

                        examinationType,

                        "文字",

                        "",

                        $"{examinationType} 教學影像資料",

                        "",

                        "",

                        "",

                        examinationType,

                        bodyPart,

                        $"{examinationType} 影像，供教學示範使用。",

                        "",

                        "",

                        "",

                        "",

                        "",

                        "",

                        "影像判讀",

                        DateTime.Now
                            .AddDays(-i)
                            .ToString(
                                "yyyy-MM-ddTHH:mm:ss"
                            )
                    );

                    importedImageCount++;
                }

                return Ok(new
                {
                    success = true,
                    patientCount = 30,
                    imageCount =
                        importedImageCount,

                    message =
                        $"已完成 30 位病人及 {importedImageCount} 張混合病歷影像匯入。"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "批次建立 30 位示範病人失敗。"
                );

                return StatusCode(
                    500,
                    new
                    {
                        success = false,
                        error =
                            "批次匯入失敗。",
                        detail =
                            ex.InnerException?.Message
                            ?? ex.Message
                    }
                );
            }
        }
        private static string GetImportedSymptomCategory(
    string examinationType)
        {
            return examinationType switch
            {
                "XRay" => "肺臟",
                "CT" => "肺臟",
                "MRI" => "腦部",
                "Ultrasound" => "肝臟",
                "Pathology" => "其他",
                "Endoscopy" => "其他",
                "PET" => "其他",
                "Mammography" => "乳癌",
                "Angiography" => "心臟",
                "Microscopy" => "其他",
                _ => "其他"
            };
        }

        private static string GetImportedBodyPart(
            string examinationType)
        {
            return examinationType switch
            {
                "XRay" => "胸部",
                "CT" => "胸部",
                "MRI" => "腦部",
                "Ultrasound" => "腹部",
                "Pathology" => "組織",
                "Endoscopy" => "消化道",
                "PET" => "全身",
                "Mammography" => "乳房",
                "Angiography" => "血管",
                "Microscopy" => "組織",
                _ => "其他"
            };
        }

        // ============================================================
        // HBase medical_media
        // 肺部 X 光 / 肺部超音波圖片本體直接存進 HBase。
        // 不把圖片寫入 App_Data 或 wwwroot。
        // ============================================================
        private sealed class HBaseMedicalMedia
        {
            public string MediaId { get; set; } = "";
            public string PatientRowKey { get; set; } = "";
            public string ExaminationType { get; set; } = "";
            public string BodyPart { get; set; } = "";
            public string SymptomCategory { get; set; } = "";
            public string FileName { get; set; } = "";
            public string ContentType { get; set; } = "";
            public string Note { get; set; } = "";
            public string ContentBase64 { get; set; } = "";
            public string CreatedAt { get; set; } = "";
        }

        private static string NormalizeHBaseExaminationType(
            string examinationType)
        {
            string value =
                examinationType?.Trim() ?? "";

            if (ContainsAny(
                    value,
                    "X光",
                    "X 光",
                    "XRay",
                    "X-Ray"))
            {
                return "xray";
            }

            if (ContainsAny(
                    value,
                    "超音波",
                    "超聲波",
                    "Ultrasound"))
            {
                return "ultrasound";
            }

            return "";
        }

        private static bool IsLungMediaCategory(
            string symptomCategory)
        {
            string value =
                symptomCategory?.Trim() ?? "";

            return ContainsAny(
                value,
                "肺",
                "lung"
            );
        }

        private async Task SaveHBaseLungMedicalImage(
            string row,
            string examinationType,
            string symptomCategory,
            IFormFile file,
            string note)
        {
            string normalizedType =
                NormalizeHBaseExaminationType(
                    examinationType
                );

            if (normalizedType != "xray"
                &&
                normalizedType != "ultrasound")
            {
                throw new InvalidOperationException(
                    "medical_media 目前只接受肺部 X 光與肺部超音波。"
                );
            }

            if (!IsLungMediaCategory(
                    symptomCategory))
            {
                throw new InvalidOperationException(
                    "請將症狀分類選擇為肺部相關分類（例如肺臟或肺癌）。"
                );
            }

            if (file == null
                || file.Length == 0)
            {
                throw new InvalidOperationException(
                    "圖片檔案不可為空。"
                );
            }

            const long maxImageSize =
                10L * 1024L * 1024L;

            if (file.Length > maxImageSize)
            {
                throw new InvalidOperationException(
                    "單張圖片不可超過 10 MB。"
                );
            }

            string contentType =
                file.ContentType?.Trim() ?? "";

            string extension =
                Path.GetExtension(
                    file.FileName
                ).ToLowerInvariant();

            bool allowedImage =
                extension == ".jpg"
                || extension == ".jpeg"
                || extension == ".png";

            if (!allowedImage)
            {
                throw new InvalidOperationException(
                    "HBase 肺部影像只支援 JPG、JPEG、PNG。"
                );
            }

            if (string.IsNullOrWhiteSpace(
                    contentType))
            {
                contentType =
                    extension == ".png"
                        ? "image/png"
                        : "image/jpeg";
            }

            byte[] bytes;

            using (
                var memoryStream =
                    new MemoryStream())
            {
                await file.CopyToAsync(
                    memoryStream
                );

                bytes =
                    memoryStream.ToArray();
            }

            // 圖片內容以 Base64 字串存進 HBase cell。
            // HBase REST XML 本身又會將 cell value 做傳輸用 Base64，
            // GetAll/讀取時 Decode() 解掉外層後，再 Convert.FromBase64String
            // 還原圖片 bytes。
            string imageBase64 =
                Convert.ToBase64String(
                    bytes
                );

            string mediaId =
                $"{row}_{normalizedType}_{Guid.NewGuid():N}";

            string createdAt =
                DateTime.Now.ToString(
                    "yyyy-MM-ddTHH:mm:ss"
                );

            string bodyPart =
                "肺部";

            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(mediaId)}"">
    <Cell column=""{ToBase64("info:patient_row_key")}"">{ToBase64(row)}</Cell>
    <Cell column=""{ToBase64("info:examination_type")}"">{ToBase64(normalizedType)}</Cell>
    <Cell column=""{ToBase64("info:body_part")}"">{ToBase64(bodyPart)}</Cell>
    <Cell column=""{ToBase64("info:symptom_category")}"">{ToBase64(symptomCategory)}</Cell>
    <Cell column=""{ToBase64("info:file_name")}"">{ToBase64(Path.GetFileName(file.FileName))}</Cell>
    <Cell column=""{ToBase64("info:content_type")}"">{ToBase64(contentType)}</Cell>
    <Cell column=""{ToBase64("info:note")}"">{ToBase64(note ?? "")}</Cell>
    <Cell column=""{ToBase64("info:created_at")}"">{ToBase64(createdAt)}</Cell>
    <Cell column=""{ToBase64("data:content_base64")}"">{ToBase64(imageBase64)}</Cell>
  </Row>
</CellSet>";

            HttpResponseMessage response =
                await _client.PutAsync(
                    $"medical_media/{Uri.EscapeDataString(mediaId)}",
                    new StringContent(
                        xml,
                        Encoding.UTF8,
                        "text/xml"
                    )
                );

            response.EnsureSuccessStatusCode();
        }

        private async Task<List<HBaseMedicalMedia>>
            GetHBaseMedicalMedia(
                string patientRowKey,
                string examinationType = "")
        {
            var result =
                new List<HBaseMedicalMedia>();

            patientRowKey =
                patientRowKey?.Trim() ?? "";

            string normalizedType =
                NormalizeHBaseExaminationType(
                    examinationType
                );

            try
            {
                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(15)
                    );

                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        "medical_media/*"
                    );

                request.Headers.Add(
                    "Accept",
                    "application/json"
                );

                HttpResponseMessage response =
                    await _client.SendAsync(
                        request,
                        cts.Token
                    );

                if (!response.IsSuccessStatusCode)
                {
                    return result;
                }

                string json =
                    await response.Content
                        .ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(
                        json))
                {
                    return result;
                }

                using JsonDocument doc =
                    JsonDocument.Parse(
                        json
                    );

                if (!doc.RootElement.TryGetProperty(
                        "Row",
                        out JsonElement rows))
                {
                    return result;
                }

                foreach (
                    JsonElement row
                    in rows.EnumerateArray())
                {
                    var item =
                        new HBaseMedicalMedia
                        {
                            MediaId =
                                Decode(
                                    row.GetProperty(
                                        "key"
                                    ).GetString()
                                    ?? ""
                                )
                        };

                    if (!row.TryGetProperty(
                            "Cell",
                            out JsonElement cells))
                    {
                        continue;
                    }

                    foreach (
                        JsonElement cell
                        in cells.EnumerateArray())
                    {
                        string column =
                            Decode(
                                cell.GetProperty(
                                    "column"
                                ).GetString()
                                ?? ""
                            );

                        string value =
                            Decode(
                                cell.GetProperty(
                                    "$"
                                ).GetString()
                                ?? ""
                            );

                        switch (column)
                        {
                            case "info:patient_row_key":
                                item.PatientRowKey = value;
                                break;

                            case "info:examination_type":
                                item.ExaminationType = value;
                                break;

                            case "info:body_part":
                                item.BodyPart = value;
                                break;

                            case "info:symptom_category":
                                item.SymptomCategory = value;
                                break;

                            case "info:file_name":
                                item.FileName = value;
                                break;

                            case "info:content_type":
                                item.ContentType = value;
                                break;

                            case "info:note":
                                item.Note = value;
                                break;

                            case "info:created_at":
                                item.CreatedAt = value;
                                break;

                            case "data:content_base64":
                                item.ContentBase64 = value;
                                break;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(
                            patientRowKey)
                        &&
                        !string.Equals(
                            item.PatientRowKey,
                            patientRowKey,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(
                            normalizedType)
                        &&
                        !string.Equals(
                            item.ExaminationType,
                            normalizedType,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    result.Add(item);
                }

                return result
                    .OrderByDescending(x =>
                        DateTime.TryParse(
                            x.CreatedAt,
                            out DateTime date
                        )
                            ? date
                            : DateTime.MinValue
                    )
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "讀取 HBase medical_media 失敗。PatientRowKey={PatientRowKey}, ExaminationType={ExaminationType}",
                    patientRowKey,
                    examinationType
                );

                return result;
            }
        }

        private async Task<HBaseMedicalMedia?>
            GetHBaseMedicalMediaById(
                string mediaId)
        {
            mediaId =
                mediaId?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(
                    mediaId))
            {
                return null;
            }

            try
            {
                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(15)
                    );

                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        $"medical_media/{Uri.EscapeDataString(mediaId)}"
                    );

                request.Headers.Add(
                    "Accept",
                    "application/json"
                );

                HttpResponseMessage response =
                    await _client.SendAsync(
                        request,
                        cts.Token
                    );

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string json =
                    await response.Content
                        .ReadAsStringAsync();

                using JsonDocument doc =
                    JsonDocument.Parse(
                        json
                    );

                if (!doc.RootElement.TryGetProperty(
                        "Row",
                        out JsonElement rows))
                {
                    return null;
                }

                JsonElement? firstRow =
                    rows.EnumerateArray()
                        .Cast<JsonElement?>()
                        .FirstOrDefault();

                if (!firstRow.HasValue)
                {
                    return null;
                }

                var item =
                    new HBaseMedicalMedia
                    {
                        MediaId = mediaId
                    };

                if (!firstRow.Value.TryGetProperty(
                        "Cell",
                        out JsonElement cells))
                {
                    return null;
                }

                foreach (
                    JsonElement cell
                    in cells.EnumerateArray())
                {
                    string column =
                        Decode(
                            cell.GetProperty(
                                "column"
                            ).GetString()
                            ?? ""
                        );

                    string value =
                        Decode(
                            cell.GetProperty(
                                "$"
                            ).GetString()
                            ?? ""
                        );

                    switch (column)
                    {
                        case "info:patient_row_key":
                            item.PatientRowKey = value;
                            break;
                        case "info:examination_type":
                            item.ExaminationType = value;
                            break;
                        case "info:body_part":
                            item.BodyPart = value;
                            break;
                        case "info:symptom_category":
                            item.SymptomCategory = value;
                            break;
                        case "info:file_name":
                            item.FileName = value;
                            break;
                        case "info:content_type":
                            item.ContentType = value;
                            break;
                        case "info:note":
                            item.Note = value;
                            break;
                        case "info:created_at":
                            item.CreatedAt = value;
                            break;
                        case "data:content_base64":
                            item.ContentBase64 = value;
                            break;
                    }
                }

                return item;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "讀取 HBase medical_media 單筆圖片失敗。MediaId={MediaId}",
                    mediaId
                );

                return null;
            }
        }

        [HttpGet]
        public async Task<IActionResult>
            HBaseMedicalMediaFile(
                string mediaId)
        {
            var block =
                RequireLogin();

            if (block != null)
                return block;

            HBaseMedicalMedia? media =
                await GetHBaseMedicalMediaById(
                    mediaId
                );

            if (media == null
                ||
                string.IsNullOrWhiteSpace(
                    media.ContentBase64))
            {
                return NotFound();
            }

            try
            {
                byte[] bytes =
                    Convert.FromBase64String(
                        media.ContentBase64
                    );

                string contentType =
                    string.IsNullOrWhiteSpace(
                        media.ContentType)
                        ? "image/jpeg"
                        : media.ContentType;

                return File(
                    bytes,
                    contentType
                );
            }
            catch (FormatException)
            {
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    "HBase 圖片資料格式錯誤。"
                );
            }
        }

        private async Task<List<object>>
    BuildAiHBaseExaminationImages(
        Cadaver patient,
        string examinationType)
        {
            var result =
                new List<object>();

            string normalizedType =
                NormalizeHBaseExaminationType(
                    examinationType
                );

            if (normalizedType != "xray"
                &&
                normalizedType != "ultrasound")
            {
                return result;
            }

            List<HBaseMedicalMedia> mediaList =
                await GetHBaseMedicalMedia(
                    patient.RowKey,
                    normalizedType
                );

            foreach (
                HBaseMedicalMedia media
                in mediaList.Take(12))
            {
                string imageUrl =
                    Url.Action(
                        "HBaseMedicalMediaFile",
                        "Home",
                        new
                        {
                            mediaId =
                                media.MediaId
                        }
                    )
                    ?? "";

                string title =
                    normalizedType == "xray"
                        ? "肺部 X 光"
                        : "肺部超音波";

                result.Add(new
                {
                    patientRowKey =
                        patient.RowKey ?? "",

                    medicalRecordNumber =
                        patient.MedicalRecordNumber
                        ?? "",

                    imageId =
                        media.MediaId,

                    imagePath =
                        imageUrl,

                    originalImagePath =
                        imageUrl,

                    pageUrl =
                        imageUrl,

                    isAnnotated =
                        false,

                    imageType =
                        normalizedType,

                    title,

                    note =
                        string.IsNullOrWhiteSpace(
                            media.Note)
                            ? $"{title}教學影像"
                            : media.Note,

                    source =
                        "hbase-medical-media"
                });
            }

            return result;
        }

        private async Task<List<object>>
    BuildAiHBaseGlobalExaminationImages(
        string examinationType)
        {
            var result =
                new List<object>();

            string normalizedType =
                NormalizeHBaseExaminationType(
                    examinationType
                );

            if (normalizedType != "xray"
                &&
                normalizedType != "ultrasound")
            {
                return result;
            }

            List<HBaseMedicalMedia> mediaList =
                await GetHBaseMedicalMedia(
                    "",
                    normalizedType
                );

            foreach (
                HBaseMedicalMedia media
                in mediaList.Take(12))
            {
                string imageUrl =
                    Url.Action(
                        "HBaseMedicalMediaFile",
                        "Home",
                        new
                        {
                            mediaId =
                                media.MediaId
                        }
                    )
                    ?? "";

                string title =
                    normalizedType == "xray"
                        ? "肺部 X 光"
                        : "肺部超音波";

                result.Add(new
                {
                    patientRowKey =
                        media.PatientRowKey ?? "",

                    medicalRecordNumber =
                        "",

                    imageId =
                        media.MediaId,

                    imagePath =
                        imageUrl,

                    originalImagePath =
                        imageUrl,

                    pageUrl =
                        imageUrl,

                    isAnnotated =
                        false,

                    imageType =
                        normalizedType,

                    title,

                    note =
                        string.IsNullOrWhiteSpace(
                            media.Note)
                            ? $"{title}教學影像"
                            : media.Note,

                    source =
                        "hbase-medical-media"
                });
            }

            return result;
        }

        private async Task<List<object>>
            BuildAiAllHBasePatientImages(
                Cadaver patient)
        {
            var result =
                new List<object>();

            List<HBaseMedicalMedia> mediaList =
                await GetHBaseMedicalMedia(
                    patient.RowKey
                );

            foreach (
                HBaseMedicalMedia media
                in mediaList.Take(12))
            {
                string imageUrl =
                    Url.Action(
                        "HBaseMedicalMediaFile",
                        "Home",
                        new
                        {
                            mediaId =
                                media.MediaId
                        }
                    )
                    ?? "";

                string title =
                    string.Equals(
                        media.ExaminationType,
                        "xray",
                        StringComparison.OrdinalIgnoreCase)
                        ? "肺部 X 光"
                        : string.Equals(
                            media.ExaminationType,
                            "ultrasound",
                            StringComparison.OrdinalIgnoreCase)
                            ? "肺部超音波"
                            : "HBase 病歷影像";

                result.Add(new
                {
                    patientRowKey =
                        patient.RowKey ?? "",

                    medicalRecordNumber =
                        patient.MedicalRecordNumber
                        ?? "",

                    imageId =
                        media.MediaId,

                    imagePath =
                        imageUrl,

                    originalImagePath =
                        imageUrl,

                    pageUrl =
                        imageUrl,

                    isAnnotated =
                        false,

                    imageType =
                        media.ExaminationType,

                    title,

                    note =
                        string.IsNullOrWhiteSpace(
                            media.Note)
                            ? $"{title}（圖片內容儲存於 HBase medical_media）"
                            : media.Note,

                    source =
                        "hbase-medical-media"
                });
            }

            return result;
        }

        private async Task SaveVideo(
    string row,
    string path,
    string note,
    string examinationType,
    string symptomCategory,
    bool isAlive,
    string videoType)
        {
            string id =
                Guid.NewGuid()
                    .ToString("N");

            videoType =
                videoType?.Trim()
                    .ToLowerInvariant()
                ?? "course";

            if (
                videoType != "course" &&
                videoType != "medical")
            {
                videoType = "course";
            }

            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">

    <Cell column=""{ToBase64($"info:video_{id}")}"">
        {ToBase64(path)}
    </Cell>

    <Cell column=""{ToBase64($"info:note_{id}")}"">
        {ToBase64(note ?? "")}
    </Cell>

    <Cell column=""{ToBase64($"info:category_{id}")}"">
        {ToBase64(symptomCategory ?? "")}
    </Cell>

    <Cell column=""{ToBase64($"info:video_type_{id}")}"">
        {ToBase64(videoType)}
    </Cell>

    <Cell column=""{ToBase64($"info:isAlive_{id}")}"">
        {ToBase64(isAlive ? "1" : "0")}
    </Cell>

    <Cell column=""{ToBase64($"info:examination_type_{id}")}"">
        {ToBase64(examinationType ?? "")}
    </Cell>

  </Row>
</CellSet>";

            using var content =
                new StringContent(
                    xml,
                    Encoding.UTF8,
                    "text/xml"
                );

            HttpResponseMessage response =
                await _client.PutAsync(
                    $"cadaver/{Uri.EscapeDataString(row)}",
                    content
                );

            response.EnsureSuccessStatusCode();
        }
        // =========================
        // 首頁
        // =========================
        [HttpGet]
        public async Task<IActionResult> Index(
    string row = "",
    string gender = "",
    string age = "",
    string donationYear = "",
    string dissectionDate = "",
    string panel = "",
    int page = 1)
        {
            var block = RequireLogin();

            if (block != null)
                return block;

            var data = await GetAllData();
            Cadaver? currentPatient =
    null;

            if (!string.IsNullOrWhiteSpace(row))
            {
                currentPatient =
                    data.FirstOrDefault(x =>
                        string.Equals(
                            x.RowKey,
                            row,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
            }

            ViewBag.CurrentPatient =
                currentPatient;
            if (TempData["PatientFormData"] is string formJson)
            {
                ViewBag.PatientFormData =
                    JsonSerializer.Deserialize<Dictionary<string, string>>(formJson)
                    ?? new Dictionary<string, string>();
            }
            else
            {
                ViewBag.PatientFormData =
                    new Dictionary<string, string>();
            }
            if (!string.IsNullOrWhiteSpace(row))
            {
                ViewBag.EditData = data.FirstOrDefault(x => x.RowKey == row);
            }
            ViewBag.OpenPanel = panel;
            ViewBag.AddPage = page;
            return View("Index_backup", data);
        }


        // =========================
        // 學生課程筆記
        // =========================
        [HttpGet]
        public async Task<IActionResult> DailyNotes(
            string noteDate = "")
        {
            var block = RequireLogin();

            if (block != null)
                return block;

            string role =
                HttpContext.Session.GetString("Role")
                ?? "";

            if (role != "Student")
            {
                if (role == "Admin")
                    return RedirectToAction(
                        "SurgicalTableManagement"
                    );

                return RedirectToAction("Index");
            }

            string studentId =
                HttpContext.Session.GetString("LoginId")
                ?? "";

            if (string.IsNullOrWhiteSpace(studentId))
                return RedirectToAction("Login");

            List<ClassSchedule> schedules =
                await GetClassSchedules();

            List<DailyNoteSurgeryDate> availableDates =
                schedules
                    .Where(x =>
                        DateTime.TryParse(
                            x.ClassDate,
                            out _
                        )
                    )
                    .GroupBy(x =>
                        DateTime.Parse(
                            x.ClassDate
                        ).ToString("yyyy-MM-dd")
                    )
                    .Select(g =>
                        new DailyNoteSurgeryDate
                        {
                            Date = g.Key,
                            ScheduleCount = g.Count(),
                            CadaverRowKeys =
                                g.Select(x =>
                                        x.CadaverRowKey)
                                 .Where(x =>
                                        !string.IsNullOrWhiteSpace(x))
                                 .Distinct(
                                     StringComparer.OrdinalIgnoreCase
                                 )
                                 .ToList()
                        }
                    )
                    .OrderByDescending(x =>
                        DateTime.Parse(x.Date)
                    )
                    .ToList();

            noteDate = noteDate?.Trim() ?? "";

            if (DateTime.TryParse(
                    noteDate,
                    out DateTime requestedDate))
            {
                noteDate =
                    requestedDate.ToString(
                        "yyyy-MM-dd"
                    );
            }
            else
            {
                noteDate = "";
            }

            bool requestedDateExists =
                availableDates.Any(x =>
                    string.Equals(
                        x.Date,
                        noteDate,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (!requestedDateExists)
            {
                string today =
                    DateTime.Today.ToString(
                        "yyyy-MM-dd"
                    );

                bool todayHasSurgery =
                    availableDates.Any(x =>
                        string.Equals(
                            x.Date,
                            today,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );

                noteDate =
                    todayHasSurgery
                        ? today
                        : availableDates
                            .FirstOrDefault()
                            ?.Date
                          ?? "";
            }

            List<StudentDailyNote> notes =
                await GetStudentDailyNotes(
                    studentId
                );

            StudentDailyNote? selected =
                notes.FirstOrDefault(x =>
                    string.Equals(
                        x.NoteDate,
                        noteDate,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            var model =
                new StudentDailyNotesViewModel
                {
                    SelectedDate =
                        noteDate,

                    CurrentContent =
                        selected?.Content ?? "",

                    CurrentUpdatedAt =
                        selected?.UpdatedAt ?? "",

                    Notes =
                        notes,

                    AvailableDates =
                        availableDates
                };

            return View(
                "DailyNotes",
                model
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveDailyNote(
            string noteDate,
            string content)
        {
            var block = RequireLogin();

            if (block != null)
                return block;

            string role =
                HttpContext.Session.GetString("Role")
                ?? "";

            if (role != "Student")
                return Forbid();

            string studentId =
                HttpContext.Session.GetString("LoginId")
                ?? "";

            if (string.IsNullOrWhiteSpace(studentId))
                return RedirectToAction("Login");

            noteDate = noteDate?.Trim() ?? "";
            content = content?.Trim() ?? "";

            if (!DateTime.TryParse(
                    noteDate,
                    out DateTime parsedDate))
            {
                TempData["Message"] =
                    "課程筆記儲存失敗：日期格式錯誤。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "DailyNotes"
                );
            }

            noteDate =
                parsedDate.ToString(
                    "yyyy-MM-dd"
                );

            // 後端再次檢查，避免學生自行修改 request，
            // 在沒有手術安排的日期建立筆記。
            List<ClassSchedule> schedules =
                await GetClassSchedules();

            bool hasSurgerySchedule =
                schedules.Any(x =>
                    DateTime.TryParse(
                        x.ClassDate,
                        out DateTime scheduleDate
                    )
                    &&
                    string.Equals(
                        scheduleDate.ToString(
                            "yyyy-MM-dd"
                        ),
                        noteDate,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (!hasSurgerySchedule)
            {
                TempData["Message"] =
                    "此日期沒有手術安排，不能建立課程筆記。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "DailyNotes"
                );
            }

            if (content.Length > 10000)
            {
                TempData["Message"] =
                    "課程筆記最多 10000 個字元。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "DailyNotes",
                    new
                    {
                        noteDate
                    }
                );
            }

            string safeStudentId =
                Regex.Replace(
                    studentId,
                    @"[^A-Za-z0-9_-]",
                    "_"
                );

            string rowKey =
    $"DAILY_NOTE_{safeStudentId}_" +
    $"{parsedDate:yyyyMMdd}_" +
    $"{DateTime.Now:HHmmssfff}_" +
    $"{Guid.NewGuid():N}";

            try
            {
                await PutStudentDailyNoteRow(
                    rowKey,
                    studentId,
                    noteDate,
                    content
                );

                TempData["Message"] =
                    "課程筆記已儲存。";

                TempData["MessageType"] =
                    "success";
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "儲存課程筆記失敗 StudentId={StudentId}, NoteDate={NoteDate}",
                    studentId,
                    noteDate
                );

                TempData["Message"] =
                    "課程筆記儲存失敗，請確認 HBase REST API 是否正常。";

                TempData["MessageType"] =
                    "error";
            }

            return RedirectToAction(
                "DailyNotes",
                new
                {
                    noteDate
                }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDailyNote(
    string rowKey,
    string noteDate)
        {
            var block = RequireLogin();

            if (block != null)
                return block;

            string role =
                HttpContext.Session.GetString("Role")
                ?? "";

            if (role != "Student")
                return Forbid();

            string studentId =
                HttpContext.Session.GetString("LoginId")
                ?? "";

            rowKey =
                rowKey?.Trim()
                ?? "";

            noteDate =
                noteDate?.Trim()
                ?? "";

            if (string.IsNullOrWhiteSpace(rowKey))
            {
                TempData["Message"] =
                    "刪除失敗：缺少筆記資料。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "DailyNotes",
                    new
                    {
                        noteDate
                    }
                );
            }

            // 先確認這筆筆記確實屬於目前登入學生
            List<StudentDailyNote> notes =
                await GetStudentDailyNotes(
                    studentId
                );

            StudentDailyNote? target =
                notes.FirstOrDefault(x =>
                    string.Equals(
                        x.RowKey,
                        rowKey,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (target == null)
            {
                TempData["Message"] =
                    "找不到這筆筆記，或你沒有刪除權限。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "DailyNotes",
                    new
                    {
                        noteDate
                    }
                );
            }

            try
            {
                HttpResponseMessage response =
                    await _client.DeleteAsync(
                        "student_course_note/" +
                        Uri.EscapeDataString(
                            rowKey
                        )
                    );

                if (
                    response.IsSuccessStatusCode
                    ||
                    response.StatusCode ==
                    System.Net.HttpStatusCode.NotFound
                )
                {
                    TempData["Message"] =
                        "筆記已刪除。";

                    TempData["MessageType"] =
                        "success";
                }
                else
                {
                    TempData["Message"] =
                        "刪除筆記失敗。";

                    TempData["MessageType"] =
                        "error";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "刪除每日筆記失敗 RowKey={RowKey}",
                    rowKey
                );

                TempData["Message"] =
                    "刪除筆記失敗，請確認 HBase REST API。";

                TempData["MessageType"] =
                    "error";
            }

            return RedirectToAction(
                "DailyNotes",
                new
                {
                    noteDate =
                        string.IsNullOrWhiteSpace(
                            noteDate
                        )
                            ? target.NoteDate
                            : noteDate
                }
            );
        }

        [HttpPost]
        public async Task<IActionResult> Save(
     string row,
     string medicalRecordNumber,
     string gender,
     string bloodType,
     string bloodTypeSource,
     string drugAllergyHistory,
     string pastMedicalHistory,
     string dissectionStatus)
        {
            var block = RequireTeacher();
            if (block != null)
                return block;

            row = row?.Trim() ?? "";
            medicalRecordNumber = medicalRecordNumber?.Trim() ?? "";
            gender = gender?.Trim() ?? "";
            bloodType = bloodType?.Trim() ?? "";
            bloodTypeSource = bloodTypeSource?.Trim() ?? "";
            drugAllergyHistory =
                drugAllergyHistory?.Trim() ?? "";
            pastMedicalHistory =
                pastMedicalHistory?.Trim() ?? "";
            dissectionStatus =
    dissectionStatus?.Trim() ?? "0";
            // ==================================================
            // Teacher 只能修改既有大體老師，不能新增
            // Admin 才能新增
            // ==================================================
            List<Cadaver> existingPatients =
                await GetAllData();

            bool patientExists =
                existingPatients.Any(x =>
                    string.Equals(
                        x.RowKey,
                        row,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (
                !patientExists
                &&
                !IsAdmin()
            )
            {
                TempData["Message"] =
                    "只有管理員可以新增大體老師資料。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "Index",
                    new
                    {
                        panel = "list"
                    }
                );
            }
            TempData["PatientFormData"] = JsonSerializer.Serialize(
    new Dictionary<string, string>
    {
        ["row"] = row,
        ["medicalRecordNumber"] = medicalRecordNumber,
        ["gender"] = gender,
        ["bloodType"] = bloodType,
        ["bloodTypeSource"] = bloodTypeSource,
        ["drugAllergyHistory"] = drugAllergyHistory,
        ["pastMedicalHistory"] = pastMedicalHistory,
        ["dissectionStatus"] = dissectionStatus
    }
);
            var requiredFields = new Dictionary<string, string>
{
    { "大體老師編號", row },
    { "病歷號碼", medicalRecordNumber },
    { "性別", gender },
    { "血型", bloodType },
    { "血型資料來源", bloodTypeSource },
    { "藥物過敏史", drugAllergyHistory },
    { "過去病史", pastMedicalHistory }
};

            var missingFields = requiredFields
                .Where(x => string.IsNullOrWhiteSpace(x.Value))
                .Select(x => x.Key)
                .ToList();

            if (missingFields.Count > 0)
            {
                TempData["Message"] =
                    "資料新增失敗，以下欄位為必填：" +
                    string.Join("、", missingFields);

                TempData["MessageType"] = "error";
                TempData["OpenPanel"] = "add";

                return RedirectToAction("Index");
            }
            // 病人編號格式
            if (!System.Text.RegularExpressions.Regex.IsMatch(
                    row,
                    @"^[A-Za-z0-9_-]{1,30}$"))
            {
                TempData["Message"] =
                    "資料新增失敗：病人編號只能包含英文字母、數字、底線及連字號。";

                TempData["MessageType"] = "error";
                TempData["OpenPanel"] = "add";

                return RedirectToAction("Index");
            }

            // 性別白名單
            string[] allowedGenders =
            {
    "男",
    "女"
};

            if (!allowedGenders.Contains(gender))
            {
                TempData["Message"] =
                    "資料新增失敗：請選擇正確的性別。";

                TempData["MessageType"] = "error";
                TempData["OpenPanel"] = "add";

                return RedirectToAction("Index");
            }            // 血型白名單
            string[] allowedBloodTypes =
            {
    "A",
    "B",
    "AB",
    "O",
    "不詳"
};

            if (!allowedBloodTypes.Contains(bloodType))
            {
                TempData["Message"] =
                    "資料新增失敗：請選擇正確的血型。";

                TempData["MessageType"] = "error";
                TempData["OpenPanel"] = "add";

                return RedirectToAction("Index");
            }            // 血型來源白名單
            string[] allowedBloodTypeSources =
            {
    "病人自述",
    "病人不知",
    "檢驗確認"
};

            if (!allowedBloodTypeSources.Contains(bloodTypeSource))
            {
                TempData["Message"] =
                    "資料新增失敗：請選擇正確的血型資料來源。";

                TempData["MessageType"] = "error";
                TempData["OpenPanel"] = "add";

                return RedirectToAction("Index");
            }
            var data = await GetAllData();

            var oldData = data.FirstOrDefault(x =>
                string.Equals(
                    x.RowKey,
                    row,
                    StringComparison.OrdinalIgnoreCase
                )
            );
            bool isNewPatient = oldData == null;

            if (isNewPatient)
            {
                bool duplicateMedicalRecordNumber = data.Any(x =>
                    string.Equals(
                        x.MedicalRecordNumber,
                        medicalRecordNumber,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                if (duplicateMedicalRecordNumber)
                {
                    TempData["Message"] =
                        "資料新增失敗：此病歷號碼已存在，請使用其他病歷號碼。";

                    TempData["MessageType"] = "error";
                    TempData["OpenPanel"] = "add";

                    return RedirectToAction("Index");
                }
            }
            else
            {
                bool duplicateMedicalRecordNumber = data.Any(x =>
                    !string.Equals(
                        x.RowKey,
                        row,
                        StringComparison.OrdinalIgnoreCase
                    )
                    &&
                    string.Equals(
                        x.MedicalRecordNumber,
                        medicalRecordNumber,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                if (duplicateMedicalRecordNumber)
                {
                    TempData["Message"] =
                        "資料修改失敗：此病歷號碼已被其他病人使用。";

                    TempData["MessageType"] = "error";
                    TempData["OpenPanel"] = "add";

                    return RedirectToAction("Index");
                }
            }
            try
            {
                await PutPatientRow(
                    row,
                    medicalRecordNumber,
                    gender,
                    bloodType,
                    bloodTypeSource,
                    drugAllergyHistory,
                    pastMedicalHistory,
                    dissectionStatus,
                    "manual",
                    DateTime.Now.ToString("yyyyMMddHHmmss")
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "新增或修改病人資料失敗，病人編號：{Row}",
                    row
                );

                TempData["Message"] =
                    "資料儲存失敗：無法寫入資料庫，請確認 HBase 服務是否正常。";

                TempData["MessageType"] = "error";
                TempData["OpenPanel"] = "add";

                return RedirectToAction("Index");
            }

            bool hasChanged =
                oldData == null ||
                oldData.MedicalRecordNumber != medicalRecordNumber ||
                oldData.Gender != gender ||
                oldData.BloodType != bloodType ||
                oldData.BloodTypeSource != bloodTypeSource ||
                oldData.DrugAllergyHistory != drugAllergyHistory ||
                oldData.PastMedicalHistory != pastMedicalHistory ||
                oldData.IsDissected != (dissectionStatus == "1");

            if (hasChanged)
            {
                string recordId =
                    Guid.NewGuid().ToString("N");

                string finding =
                    $"性別={gender}；" +
                    $"病歷號={medicalRecordNumber}；" +
                    $"血型={bloodType}";

                string note = oldData == null
                    ? "新增大體老師資料基本資料。"
                    : "修改大體老師資料基本資料。";

                await PutMedicalRecord(
                    row,
                    new MedicalRecord
                    {
                        Id = recordId,
                        ResourceType = "Observation",
                        OccurredAt =
                            DateTime.Now.ToString(
                                "yyyy-MM-ddTHH:mm:ss"
                            ),
                        Diagnosis = oldData == null
                            ? "PatientCreated"
                            : "PatientUpdated",
                        BodyPart = "病人基本資料",
                        Finding = finding,
                        Treatment = "",
                        Physician = "系統",
                        Note = note,
                        FhirJson = BuildObservationFhirJson(
                            row,
                            recordId,
                            DateTime.Now.ToString(
                                "yyyy-MM-ddTHH:mm:ss"
                            ),
                            oldData == null
                                ? "PatientCreated"
                                : "PatientUpdated",
                            "病人基本資料",
                            finding,
                            "",
                            "系統",
                            note
                        )
                    }
                );
            }
            TempData["Message"] = oldData == null
                ? "病人資料已新增"
                : "病人資料已修改";

            TempData["MessageType"] = "success";

            return RedirectToAction(
                "Index",
                new
                {
                    row = row,
                    panel = "add",
                    page = 2
                }
            );
        }
        private static List<string> ExtractSearchKeywords(
            string question)
        {
            if (string.IsNullOrWhiteSpace(question))
            {
                return new List<string>();
            }

            string normalized =
                question.Trim();

            string[] stopWords =
            {
        "請問",
        "請查詢",
        "查詢",
        "搜尋",
        "找出",
        "幫我找",
        "帮我找",
        "找一下",
        "查一下",
        "我想看",
        "想看",
        "有沒有人的",
        "有哪些人的",
        "有哪些",
        "哪些",
        "哪位",
        "病人",
        "患者",
        "病患",
        "病例",
        "病歷",
        "資料",
        "內容",
        "紀錄",
        "相關",
        "所有",
        "什麼",
        "是否",
        "有沒有",
        "幫我",
        "一下",
        "請",
        "的"
    };

            foreach (string stopWord in stopWords)
            {
                normalized =
                    normalized.Replace(
                        stopWord,
                        " ",
                        StringComparison.OrdinalIgnoreCase
                    );
            }

            char[] separators =
            {
        ' ',
        '\t',
        '\r',
        '\n',
        '，',
        '。',
        '、',
        '；',
        '：',
        '？',
        '！',
        ',',
        '.',
        ';',
        ':',
        '?',
        '!',
        '(',
        ')',
        '（',
        '）'
    };

            List<string> segments =
                normalized
                    .Split(
                        separators,
                        StringSplitOptions.RemoveEmptyEntries
                    )
                    .Select(x => x.Trim())
                    .Where(x => x.Length >= 2)
                    .ToList();

            var keywords =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (string segment in segments)
            {
                keywords.Add(segment);

                int maxLength =
                    Math.Min(4, segment.Length);

                for (
                    int length = 2;
                    length <= maxLength;
                    length++)
                {
                    for (
                        int start = 0;
                        start <= segment.Length - length;
                        start++)
                    {
                        keywords.Add(
                            segment.Substring(
                                start,
                                length
                            )
                        );
                    }
                }
            }

            return keywords
                .OrderByDescending(x => x.Length)
                .Take(40)
                .ToList();
        }
        private static int CalculatePatientMatchScore(
            Cadaver patient,
            string question,
            List<string> keywords)
        {
            int score = 0;

            int MatchText(
                string? value,
                int weight = 1)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return 0;
                }

                string text = value.Trim();
                int localScore = 0;

                // 問題直接指定 RowKey、病歷號碼或完整欄位值
                if (question.Contains(
                        text,
                        StringComparison.OrdinalIgnoreCase))
                {
                    localScore += 10 * weight;
                }

                // 病歷欄位直接包含完整問題
                if (text.Contains(
                        question,
                        StringComparison.OrdinalIgnoreCase))
                {
                    localScore += 8 * weight;
                }

                foreach (string keyword in keywords)
                {
                    if (text.Contains(
                            keyword,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        localScore += 3 * weight;
                    }
                }

                return localScore;
            }

            // 病人識別欄位權重較高
            score += MatchText(patient.RowKey, 4);
            score += MatchText(
                patient.MedicalRecordNumber,
                4
            );

            score += MatchText(patient.Gender);
            score += MatchText(patient.Age);
            score += MatchText(patient.BloodType);

            score += MatchText(
                patient.DrugAllergyHistory,
                2
            );

            score += MatchText(
                patient.PastMedicalHistory,
                2
            );

            score += MatchText(
                patient.AdmissionReason,
                2
            );

            score += MatchText(
                patient.AdmissionDiagnosisCode,
                2
            );

            score += MatchText(
                patient.PrimaryDiagnosisCode,
                3
            );

            score += MatchText(
                patient.SecondaryDiagnosisCode,
                2
            );

            score += MatchText(
                patient.DischargeDiagnosisCode,
                2
            );

            score += MatchText(
                patient.DischargeStatus
            );

            score += MatchText(
                patient.DissectionProcessRecord,
                2
            );

            score += MatchText(
                patient.TeacherExplanationRecord,
                2
            );

            score += MatchText(
                patient.TeachingNote,
                2
            );

            score += MatchText(
                patient.PathologicalFindings,
                3
            );

            score += MatchText(
                patient.RelatedDiagnosisCode,
                2
            );

            foreach (
                MedicalRecord record
                in patient.MedicalRecords
                    ?? new List<MedicalRecord>())
            {
                score += MatchText(record.OccurredAt);
                score += MatchText(record.Diagnosis, 3);
                score += MatchText(record.BodyPart, 2);
                score += MatchText(record.Finding, 3);
                score += MatchText(record.Treatment, 2);
                score += MatchText(record.Physician);
                score += MatchText(record.Note, 2);
            }

            foreach (
                LabTestRecord lab
                in patient.LabTestRecords
                    ?? new List<LabTestRecord>())
            {
                score += MatchText(lab.TestDate);
                score += MatchText(lab.TestItem, 2);
                score += MatchText(
                    lab.TestItemCode,
                    2
                );
                score += MatchText(
                    lab.TestItemName,
                    3
                );
                score += MatchText(
                    lab.TestResult,
                    2
                );
                score += MatchText(
                    lab.NumericResult,
                    2
                );
                score += MatchText(
                    lab.TextResult,
                    2
                );
                score += MatchText(
                    lab.ReferenceRange
                );
                score += MatchText(
                    lab.AbnormalFlag,
                    2
                );
                score += MatchText(
                    lab.TestMethod
                );
            }

            return score;
        }
        private static string RedactSensitiveText(
    string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            string result = text;

            // 姓名=王小明、姓名：王小明
            result =
                System.Text.RegularExpressions.Regex.Replace(
                    result,
                    @"姓名\s*[=:：]\s*[^；;,，\r\n]+",
                    "姓名=[已隱藏]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );

            // 電話=0912345678、電話：0912-345-678
            result =
                System.Text.RegularExpressions.Regex.Replace(
                    result,
                    @"電話\s*[=:：]\s*[0-9+\-\s()]+",
                    "電話=[已隱藏]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );

            // 台灣身分證字號
            result =
                System.Text.RegularExpressions.Regex.Replace(
                    result,
                    @"\b[A-Z][12][0-9]{8}\b",
                    "[身分證字號已隱藏]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );

            // 可能直接出現的手機號碼
            result =
                System.Text.RegularExpressions.Regex.Replace(
                    result,
                    @"\b09[0-9]{8}\b",
                    "[電話已隱藏]"
                );

            return result;
        }
        // =========================
        // AI：找出與目前問題相關的病人
        // 用於 AI 回答後顯示該病人的病歷影像
        // =========================
        private List<Cadaver> FindAiMatchedPatients(
            string question,
            List<Cadaver> patients)
        {
            if (string.IsNullOrWhiteSpace(question) ||
                patients == null ||
                patients.Count == 0)
            {
                return new List<Cadaver>();
            }

            question = question.Trim();

            List<string> keywords =
                ExtractSearchKeywords(question);

            // 先做「完整識別碼」比對，避免查 P002 時，
            // 因為字串中包含 "2" 而錯抓 RowKey = "2"。
            var identifierTokens =
                Regex.Matches(
                    question,
                    @"(?<![A-Za-z0-9_-])[A-Za-z]*\d+[A-Za-z0-9_-]*(?![A-Za-z0-9_-])",
                    RegexOptions.IgnoreCase
                )
                .Select(x =>
                    x.Value.Trim()
                )
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x)
                )
                .Distinct(
                    StringComparer.OrdinalIgnoreCase
                )
                .ToList();

            if (identifierTokens.Count > 0)
            {
                List<Cadaver> exactIdentifierMatches =
                    patients
                        .Where(patient =>
                            identifierTokens.Any(token =>
                                string.Equals(
                                    patient.RowKey?.Trim(),
                                    token,
                                    StringComparison.OrdinalIgnoreCase
                                )
                                ||
                                string.Equals(
                                    patient.MedicalRecordNumber?.Trim(),
                                    token,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                        )
                        .Take(5)
                        .ToList();

                if (exactIdentifierMatches.Count > 0)
                {
                    return exactIdentifierMatches;
                }

                // 問題裡既然明確帶了識別碼，但資料庫找不到完全相同的，
                // 就不要退回模糊比對，避免 P002 被配成 2。
                return new List<Cadaver>();
            }

            List<Cadaver> exactMatchedPatients =
                patients
                    .Where(patient =>
                        (
                            !string.IsNullOrWhiteSpace(
                                patient.RowKey
                            )
                            &&
                            Regex.IsMatch(
                                question,
                                $@"(?<![A-Za-z0-9_-]){Regex.Escape(patient.RowKey.Trim())}(?![A-Za-z0-9_-])",
                                RegexOptions.IgnoreCase
                            )
                        )
                        ||
                        (
                            !string.IsNullOrWhiteSpace(
                                patient.MedicalRecordNumber
                            )
                            &&
                            Regex.IsMatch(
                                question,
                                $@"(?<![A-Za-z0-9_-]){Regex.Escape(patient.MedicalRecordNumber.Trim())}(?![A-Za-z0-9_-])",
                                RegexOptions.IgnoreCase
                            )
                        )
                    )
                    .Take(5)
                    .ToList();

            if (exactMatchedPatients.Count > 0)
            {
                return exactMatchedPatients;
            }

            bool isHypotheticalRangeQuery =
                question.Contains(
                    "如果",
                    StringComparison.OrdinalIgnoreCase
                )
                ||
                question.Contains(
                    "假設",
                    StringComparison.OrdinalIgnoreCase
                )
                ||
                question.Contains(
                    "假如",
                    StringComparison.OrdinalIgnoreCase
                );

            // 假設性問題不是特定病人的查詢，不顯示病歷影像。
            if (isHypotheticalRangeQuery)
            {
                return new List<Cadaver>();
            }

            return patients
                .Select(patient => new
                {
                    Patient = patient,
                    Score = CalculatePatientMatchScore(
                        patient,
                        question,
                        keywords
                    )
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(5)
                .Select(x => x.Patient)
                .ToList();
        }

        // ============================================================
        // AI：檢驗查詢精準處理
        // ============================================================
        private static bool IsDisplayableImagePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            path = path.Trim();
            return path.StartsWith("/", StringComparison.Ordinal)
                || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasUsefulLabContent(
            LabTestRecord lab)
        {
            bool hasResult =
                !string.IsNullOrWhiteSpace(
                    lab.NumericResult)
                ||
                !string.IsNullOrWhiteSpace(
                    lab.TextResult)
                ||
                !string.IsNullOrWhiteSpace(
                    lab.TestResult)
                ||
                !string.IsNullOrWhiteSpace(
                    lab.ReferenceRange)
                ||
                !string.IsNullOrWhiteSpace(
                    lab.AbnormalFlag);

            string name =
                !string.IsNullOrWhiteSpace(
                    lab.TestItemName)
                    ? lab.TestItemName.Trim()
                    : !string.IsNullOrWhiteSpace(
                        lab.TestItem)
                        ? lab.TestItem.Trim()
                        : lab.TestItemCode?.Trim() ?? "";

            bool hasReadableName =
                !string.IsNullOrWhiteSpace(name)
                &&
                !Regex.IsMatch(
                    name,
                    @"^\d+$"
                );

            return
                hasResult
                ||
                hasReadableName;
        }

        private static string GetLabDisplayName(LabTestRecord lab)
        {
            string value =
                !string.IsNullOrWhiteSpace(
                    lab.TestItemName)
                    ? lab.TestItemName.Trim()
                    : !string.IsNullOrWhiteSpace(
                        lab.TestItem)
                        ? lab.TestItem.Trim()
                        : lab.TestItemCode?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(value))
                return "未提供檢驗項目名稱";

            if (Regex.IsMatch(
                    value,
                    @"^\d+$"))
            {
                return
                    $"未提供可辨識的檢驗項目名稱（原始值：{value}）";
            }

            return value;
        }

        private static string GetLabResultText(LabTestRecord lab)
        {
            if (!string.IsNullOrWhiteSpace(lab.NumericResult))
            {
                string unit = lab.Unit?.Trim() ?? "";
                return string.IsNullOrWhiteSpace(unit) ? lab.NumericResult.Trim() : $"{lab.NumericResult.Trim()} {unit}";
            }
            if (!string.IsNullOrWhiteSpace(lab.TextResult)) return lab.TextResult.Trim();
            if (!string.IsNullOrWhiteSpace(lab.TestResult)) return lab.TestResult.Trim();
            return "未提供";
        }

        private static bool TryParseNumber(string? text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            Match match = Regex.Match(text, @"-?\d+(?:\.\d+)?");
            return match.Success && double.TryParse(
                match.Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        private static bool TryParseReferenceRange(string? text, out double lower, out double upper)
        {
            lower = 0; upper = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            Match match = Regex.Match(text, @"(-?\d+(?:\.\d+)?)\s*[–—－~\-～至]\s*(-?\d+(?:\.\d+)?)");
            if (!match.Success) return false;
            bool a = double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lower);
            bool b = double.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out upper);
            if (!a || !b) return false;
            if (lower > upper) (lower, upper) = (upper, lower);
            return true;
        }

        private static bool LabMatchesQuestion(LabTestRecord lab, string question)
        {
            if (string.IsNullOrWhiteSpace(question)) return false;
            string[] values = { lab.TestItem ?? "", lab.TestItemName ?? "", lab.TestItemCode ?? "" };
            foreach (string value in values)
            {
                string v = value.Trim();
                if (!string.IsNullOrWhiteSpace(v) && question.Contains(v, StringComparison.OrdinalIgnoreCase)) return true;
            }
            if (question.Contains("CBC", StringComparison.OrdinalIgnoreCase))
                return values.Any(v => v.Contains("CBC", StringComparison.OrdinalIgnoreCase));
            return false;
        }

        private IActionResult? TryHandleLabQuery(string question, AiIntentResult intent, List<Cadaver> allPatients)
        {
            if (intent.Intent != "lab_query") return null;

            bool asksAboveRange = ContainsAny(
                question,
                "超過參考範圍", "高於參考範圍", "超出參考範圍上限",
                "高於上限", "超過上限",
                "偏高", "過高", "太高", "數值高", "檢驗值高", "超標"
            );

            bool asksBelowRange = ContainsAny(
                question,
                "低於參考範圍", "低於參考範圍下限", "低於下限",
                "偏低", "過低", "太低", "數值低", "檢驗值低"
            );

            bool asksWithinRange = ContainsAny(
                question,
                "位於參考範圍內", "在參考範圍內", "落在參考範圍內",
                "符合參考範圍", "參考範圍內", "正常範圍內"
            );

            bool asksNormal =
    !ContainsAny(
        question,
        "不正常",
        "異常"
    )
    &&
    ContainsAny(
        question,
        "正常",
        "正常值",
        "檢驗正常",
        "結果正常",
        "數值正常",
        "檢查正常",
        "檢驗結果正常"
    );

            bool asksAbnormal = ContainsAny(
                question,
                "異常檢驗",
                "異常檢驗結果",
                "哪些檢驗異常",
                "哪些檢驗項目異常",
                "檢驗項目異常",
                "異常檢驗項目",
                "檢驗異常",
                "數值異常",
                "結果異常",
                "不正常",
                "有問題",
                "檢查結果有問題",
                "檢驗結果有問題"
            );

           
            bool explicitlyAsksAcrossPatients =
                ContainsAny(
                    question,
                    "哪些病人",
                    "哪些患者",
                    "哪些病患",
                    "所有病人",
                    "所有患者",
                    "所有病患",
                    "全部病人",
                    "全體病人",
                    "有哪些病人",
                    "有哪些患者",
                    "有哪些病患",
                    "哪些人的",
                    "有哪些人的",
                    "有沒有人的",
                    "相關病歷",
                    "相關病例",
                    "病例",
                    "病歷",
                    "幫我找",
                    "帮我找",
                    "找一下",
                    "查一下"
                );

            
            if (string.IsNullOrWhiteSpace(intent.PatientId)
                &&
                (
                    asksAboveRange
                    || asksBelowRange
                    || asksWithinRange
                    || asksAbnormal
                )
                &&
                !explicitlyAsksAcrossPatients)
            {
                return Ok(new
                {
                    answer =
                        "請先指定病人編號或病歷號碼，我會只查該病人的檢驗項目。若要查所有病人，請明確輸入「哪些病人的檢驗項目異常」。",
                    images = new List<object>(),
                    videos = new List<object>()
                });
            }

            if (string.IsNullOrWhiteSpace(intent.PatientId)
                &&
                explicitlyAsksAcrossPatients
                &&
                (
                    asksAboveRange
                    || asksBelowRange
                    || asksWithinRange
                    || asksNormal
                    || asksAbnormal
                ))
            {
                var matched = new List<string>();
                foreach (Cadaver patient in allPatients)
                {
                    foreach (LabTestRecord lab in patient.LabTestRecords ?? new List<LabTestRecord>())
                    {
                        bool hasNumeric =
                            TryParseNumber(
                                lab.NumericResult,
                                out double numeric
                            );

                        bool hasRange =
                            TryParseReferenceRange(
                                lab.ReferenceRange,
                                out double lower,
                                out double upper
                            );

                        string rawFlag =
                            lab.AbnormalFlag?.Trim() ?? "";

                        bool explicitAbnormalFlag =
                            rawFlag.Equals("H", StringComparison.OrdinalIgnoreCase)
                            || rawFlag.Equals("L", StringComparison.OrdinalIgnoreCase)
                            || rawFlag.Equals("HIGH", StringComparison.OrdinalIgnoreCase)
                            || rawFlag.Equals("LOW", StringComparison.OrdinalIgnoreCase)
                            || rawFlag.Equals("ABNORMAL", StringComparison.OrdinalIgnoreCase)
                            || rawFlag.Contains("異常", StringComparison.OrdinalIgnoreCase);

                        bool ok = false;

                        if (asksAboveRange)
                        {
                            ok =
                                hasNumeric
                                && hasRange
                                && numeric > upper;
                        }
                        else if (asksBelowRange)
                        {
                            ok =
                                hasNumeric
                                && hasRange
                                && numeric < lower;
                        }
                        else if (asksAbnormal)
                        {
                            ok =
                                hasNumeric
                                && hasRange
                                && (numeric < lower || numeric > upper);
                        }
                        else if (asksWithinRange || asksNormal)
                        {
                            ok =
                                hasNumeric
                                && hasRange
                                && numeric >= lower
                                && numeric <= upper;
                        }

                        if (!ok)
                            continue;

                        string date =
                            string.IsNullOrWhiteSpace(lab.TestDate)
                                ? "日期未提供"
                                : lab.TestDate.Trim();

                        string resultText =
                            GetLabResultText(lab);

                        string rangeText =
                            string.IsNullOrWhiteSpace(lab.ReferenceRange)
                                ? "未提供"
                                : lab.ReferenceRange.Trim();

                        string flagText =
                            string.IsNullOrWhiteSpace(rawFlag)
                                ? ""
                                : $"，異常標記={rawFlag}";

                        matched.Add(
                            $"{patient.RowKey}：{date}，{GetLabDisplayName(lab)}，結果={resultText}，參考範圍={rangeText}{flagText}"
                        );
                    }
                }
                if (matched.Count == 0)
                {
                    string condition =
        asksAboveRange
        ? "高於參考範圍上限"
        : asksBelowRange
            ? "低於參考範圍下限"
            : asksAbnormal
                ? "依數值與參考範圍比較顯示異常"
                : "位於參考範圍內";
                    return Ok(new
                    {
                        answer = asksAbnormal
                        ? $"目前沒有找到{condition}的檢驗紀錄。"
                        : $"目前沒有找到{condition}且 NumericResult 與 ReferenceRange 均可解析的檢驗紀錄。",
                        images = new List<object>()
                    });
                }
                string header =
    asksAboveRange
        ? "以下檢驗數值高於參考範圍上限："
        : asksBelowRange
            ? "以下檢驗數值低於參考範圍下限："
            : asksAbnormal
                ? "以下檢驗依目前數值與參考範圍比較顯示異常："
                : "以下檢驗數值位於參考範圍內：";
                return Ok(new { answer = header + "\n" + string.Join("\n", matched), images = new List<object>() });
            }

            if (!string.IsNullOrWhiteSpace(intent.PatientId))
            {
                Cadaver? patient = FindPatientByIdentifier(intent.PatientId, allPatients);
                if (patient == null)
                    return Ok(new { answer = $"找不到病人編號或病歷號碼 {intent.PatientId}。", images = new List<object>() });

                List<LabTestRecord> labs = patient.LabTestRecords ?? new List<LabTestRecord>();
                if (labs.Count == 0)
                    return Ok(new { answer = $"病人編號 {patient.RowKey} 目前沒有檢驗紀錄。", images = new List<object>() });

                // 指定病人詢問「哪些檢驗項目異常」時，
                // 只列出該病人的異常項目，不會再跳出其他病人。
                bool asksPatientAbnormalItems =
                    ContainsAny(
                        question,
                        "哪些檢驗異常",
                        "哪些檢驗項目異常",
                        "檢驗項目異常",
                        "異常檢驗項目",
                        "有哪些異常",
                        "異常檢驗"
                    );

                if (asksPatientAbnormalItems)
                {
                    var abnormalLines =
                        new List<string>();

                    foreach (LabTestRecord lab in labs)
                    {
                        bool hasNumeric =
                            TryParseNumber(
                                lab.NumericResult,
                                out double numeric
                            );

                        bool hasRange =
                            TryParseReferenceRange(
                                lab.ReferenceRange,
                                out double lower,
                                out double upper
                            );

                        string rawFlag =
                            lab.AbnormalFlag?.Trim() ?? "";

                        bool explicitAbnormalFlag =
                            rawFlag.Equals("H", StringComparison.OrdinalIgnoreCase)
                            || rawFlag.Equals("L", StringComparison.OrdinalIgnoreCase)
                            || rawFlag.Equals("HIGH", StringComparison.OrdinalIgnoreCase)
                            || rawFlag.Equals("LOW", StringComparison.OrdinalIgnoreCase)
                            || rawFlag.Equals("ABNORMAL", StringComparison.OrdinalIgnoreCase)
                            || rawFlag.Contains("異常", StringComparison.OrdinalIgnoreCase);

                        

                        if (!hasNumeric || !hasRange)
                        {
                            continue;
                        }

                        bool outsideRange =
                            numeric < lower
                            || numeric > upper;

                        if (!outsideRange)
                        {
                            continue;
                        }

                        string date =
                            string.IsNullOrWhiteSpace(lab.TestDate)
                                ? "日期未提供"
                                : lab.TestDate.Trim();

                        string resultText =
                            GetLabResultText(lab);

                        string rangeText =
                            string.IsNullOrWhiteSpace(lab.ReferenceRange)
                                ? "未提供"
                                : lab.ReferenceRange.Trim();

                        string flagText =
                            string.IsNullOrWhiteSpace(rawFlag)
                                ? ""
                                : $"，異常標記={rawFlag}";

                        abnormalLines.Add(
                            $"{date}，{GetLabDisplayName(lab)}，結果={resultText}，參考範圍={rangeText}{flagText}"
                        );
                    }

                    if (abnormalLines.Count == 0)
                    {
                        return Ok(new
                        {
                            answer =
                                $"病人編號 {patient.RowKey} 目前沒有找到具有明確異常標記，或數值超出參考範圍的檢驗項目。",
                            images = new List<object>(),
                            videos = new List<object>()
                        });
                    }

                    return Ok(new
                    {
                        answer =
                            $"病人編號 {patient.RowKey} 的異常檢驗項目如下：\n"
                            + string.Join("\n", abnormalLines),

                        images =
                            BuildAiLabImages(
                                patient,
                                question
                            ),

                        videos = new List<object>()
                    });
                }

                bool asksReferenceRange =
                    ContainsAny(
                        question,
                        "參考範圍",
                        "參考值",
                        "範圍呢"
                    );

                bool asksSpecificAbnormal =
                    ContainsAny(
                        question,
                        "異常",
                        "正常"
                    );

                if (asksReferenceRange ||
                    asksSpecificAbnormal)
                {
                    List<LabTestRecord> meaningfulLabs =
                        labs
                            .Where(
                                HasUsefulLabContent
                            )
                            .OrderByDescending(lab =>
                                DateTime.TryParse(
                                    lab.TestDate,
                                    out DateTime date)
                                    ? date
                                    : DateTime.MinValue
                            )
                            .ToList();

                    LabTestRecord latest =
                        meaningfulLabs.FirstOrDefault()
                        ?? labs
                            .OrderByDescending(lab =>
                                DateTime.TryParse(
                                    lab.TestDate,
                                    out DateTime date)
                                    ? date
                                    : DateTime.MinValue
                            )
                            .First();

                    string name =
                        GetLabDisplayName(latest);

                    if (asksReferenceRange)
                    {
                        string range =
                            string.IsNullOrWhiteSpace(
                                latest.ReferenceRange)
                                ? "未提供"
                                : latest.ReferenceRange.Trim();

                        return Ok(new
                        {
                            answer =
                                $"病人編號 {patient.RowKey} 最近一次相關檢驗「{name}」的參考範圍為 {range}。",

                            images =
                                BuildAiLabImages(
                                    patient,
                                    question
                                ),

                            videos =
                                new List<object>()
                        });
                    }

                    string conclusion ="目前缺少可解析的數值結果或參考範圍，無法確認正常或異常";

                    if (TryParseNumber(
                            latest.NumericResult,
                            out double numeric)
                        &&
                        TryParseReferenceRange(
                            latest.ReferenceRange,
                            out double lower,
                            out double upper))
                    {
                        conclusion =
                            numeric < lower || numeric > upper
                                ? "依目前數值與參考範圍比較，顯示超出參考範圍"
                                : "依目前數值與參考範圍比較，未顯示超出參考範圍";
                    }

                    return Ok(new
                    {
                        answer =
                            $"病人編號 {patient.RowKey} 最近一次相關檢驗「{name}」：{conclusion}。",

                        images =
                            BuildAiLabImages(
                                patient,
                                question
                            ),

                        videos =
                            new List<object>()
                    });
                }

                bool asksLatest = ContainsAny(question, "最近一次", "最新一次", "最近的", "最新的");
                if (asksLatest)
                {
                    // 優先選擇真正具有檢驗項目或結果內容的紀錄，
                    // 避免只存了日期／影像路徑的空白紀錄被當成「最近一次檢驗」。
                    List<LabTestRecord> meaningfulLabs =
                        labs
                            .Where(
                                HasUsefulLabContent
                            )
                            .ToList();

                    List<LabTestRecord> candidates =
                        meaningfulLabs.Count > 0
                            ? meaningfulLabs
                            : labs;

                    LabTestRecord latest =
                        candidates
                            .OrderByDescending(lab =>
                                DateTime.TryParse(
                                    lab.TestDate,
                                    out DateTime d)
                                    ? d
                                    : DateTime.MinValue
                            )
                            .First();

                    string dateText =
                        string.IsNullOrWhiteSpace(latest.TestDate)
                            ? "日期未提供"
                            : latest.TestDate;

                    string answer =
                        $"病人編號 {patient.RowKey} 最近一次具有檢驗內容的紀錄於 {dateText} 進行；" +
                        $"檢驗項目：{GetLabDisplayName(latest)}；" +
                        $"結果：{GetLabResultText(latest)}";

                    if (!string.IsNullOrWhiteSpace(latest.ReferenceRange))
                        answer += $"；參考範圍：{latest.ReferenceRange}";

                    if (!string.IsNullOrWhiteSpace(latest.AbnormalFlag))
                        answer += $"；異常標記：{latest.AbnormalFlag}";

                    answer += "。";

                    return Ok(new
                    {
                        answer,

                        images =
                            BuildAiLabImages(
                                patient,
                                question
                            ),

                        videos =
                            new List<object>()
                    });
                }

                List<LabTestRecord> specificLabs = labs
                    .Where(lab => LabMatchesQuestion(lab, question))
                    .OrderByDescending(lab => DateTime.TryParse(lab.TestDate, out DateTime d) ? d : DateTime.MinValue)
                    .ToList();

                if (specificLabs.Count > 0)
                {
                    string lines = string.Join("\n", specificLabs.Select(lab => {
                        string line = $"{lab.TestDate}：{GetLabDisplayName(lab)}，結果={GetLabResultText(lab)}";
                        if (!string.IsNullOrWhiteSpace(lab.ReferenceRange)) line += $"，參考範圍={lab.ReferenceRange}";
                        if (!string.IsNullOrWhiteSpace(lab.AbnormalFlag)) line += $"，異常標記={lab.AbnormalFlag}";
                        return line;
                    }));
                    return Ok(new
                    {
                        answer =
                            $"病人編號 {patient.RowKey} 的相關檢驗紀錄：\n{lines}",

                        images =
                            BuildAiLabImages(
                                patient,
                                question
                            ),

                        videos =
                            new List<object>()
                    });
                }

                // 使用者明確指定 CBC，但資料中沒有任何 CBC 對應紀錄時，
                // 直接回覆沒有紀錄，不再交給 Ollama 產生模糊的「查無相關資訊」。
                if (question.Contains(
                        "CBC",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return Ok(new
                    {
                        answer =
                            $"病人編號 {patient.RowKey} 目前沒有找到 TestItem、TestItemName 或 TestItemCode 為 CBC 的檢驗紀錄。",
                        images = new List<object>()
                    });
                }
            }
            return null;
        }

        private IActionResult? TryHandlePatientDetailQuery(
            string question,
            AiIntentResult intent,
            List<Cadaver> allPatients)
        {
            if (intent.Intent != "patient_detail"
                ||
                string.IsNullOrWhiteSpace(
                    intent.PatientId))
            {
                return null;
            }

            Cadaver? patient =
                FindPatientByIdentifier(
                    intent.PatientId,
                    allPatients
                );

            if (patient == null)
            {
                return Ok(new
                {
                    answer =
                        $"找不到病人編號或病歷號碼 {intent.PatientId}。",
                    images =
                        new List<object>()
                });
            }

            bool asksMedicalRecord =
                ContainsAny(
                    question,
                    "病歷",
                    "病歷呢",
                    "他的病歷",
                    "病歷資料"
                );

            if (!asksMedicalRecord)
                return null;

            List<MedicalRecord> clinicalRecords =
                (patient.MedicalRecords
                    ?? new List<MedicalRecord>())
                    .Where(record =>
                        !string.Equals(
                            record.Diagnosis,
                            "PatientCreated",
                            StringComparison.OrdinalIgnoreCase
                        )
                        &&
                        !string.Equals(
                            record.Diagnosis,
                            "PatientUpdated",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();

            List<LabTestRecord> labRecords =
                patient.LabTestRecords
                ?? new List<LabTestRecord>();

            bool hasAdmission =
                !string.IsNullOrWhiteSpace(
                    patient.AdmissionStartDate)
                ||
                !string.IsNullOrWhiteSpace(
                    patient.AdmissionReason)
                ||
                !string.IsNullOrWhiteSpace(
                    patient.PrimaryDiagnosisCode);

            var parts =
                new List<string>();

            if (clinicalRecords.Count > 0)
            {
                parts.Add(
                    $"臨床病歷 {clinicalRecords.Count} 筆"
                );
            }
            else
            {
                parts.Add(
                    "目前沒有臨床病歷紀錄"
                );
            }

            if (labRecords.Count > 0)
            {
                parts.Add(
                    $"檢驗紀錄 {labRecords.Count} 筆"
                );
            }

            if (hasAdmission)
            {
                parts.Add(
                    "有住院資料"
                );
            }

            string answer =
                $"病人編號 {patient.RowKey}："
                + string.Join(
                    "；",
                    parts
                )
                + "。";

            if (clinicalRecords.Count > 0)
            {
                MedicalRecord latest =
                    clinicalRecords
                        .OrderByDescending(record =>
                            DateTime.TryParse(
                                record.OccurredAt,
                                out DateTime date)
                                ? date
                                : DateTime.MinValue
                        )
                        .First();

                answer +=
                    $" 最近一筆臨床病歷日期為 {latest.OccurredAt}";

                if (!string.IsNullOrWhiteSpace(
                        latest.Diagnosis))
                {
                    answer +=
                        $"，診斷為 {latest.Diagnosis}";
                }

                answer += "。";
            }

            return Ok(new
            {
                answer,

                images =
                    BuildAiPatientImages(
                        patient.RowKey,
                        new List<Cadaver>
                        {
                            patient
                        }
                    ),
                videos =
    new List<object>()
            });
        }

        private bool PatientHasAnyImage(
            Cadaver patient)
        {
            bool hasMedicalImage =
                patient.MedicalImagePaths != null
                &&
                patient.MedicalImagePaths.Any(
                    IsDisplayableImagePath
                );

            bool hasAnnotatedImage =
                patient.MedicalAnnotatedImagePaths != null
                &&
                patient.MedicalAnnotatedImagePaths.Any(
                    IsDisplayableImagePath
                );

            bool hasLabImage =
                patient.LabTestRecords != null
                &&
                patient.LabTestRecords.Any(x =>
                    IsDisplayableImagePath(
                        x.ImageResultPath
                    )
                );

            return
                hasMedicalImage
                ||
                hasAnnotatedImage
                ||
                hasLabImage;
        }

        private int CountPatientDisplayImages(
            Cadaver patient)
        {
            var paths =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase
                );

            List<string> originals =
                patient.MedicalImagePaths
                ?? new List<string>();

            List<string> annotated =
                patient.MedicalAnnotatedImagePaths
                ?? new List<string>();

            for (
                int i = 0;
                i < originals.Count;
                i++)
            {
                string originalPath =
                    originals[i]?.Trim()
                    ?? "";

                string annotatedPath =
                    i < annotated.Count
                        ? annotated[i]?.Trim()
                            ?? ""
                        : "";

                string displayPath =
                    !string.IsNullOrWhiteSpace(
                        annotatedPath
                    )
                        ? annotatedPath
                        : originalPath;

                if (IsDisplayableImagePath(
                        displayPath))
                {
                    paths.Add(displayPath);
                }
            }

            if (patient.LabTestRecords != null)
            {
                foreach (
                    LabTestRecord lab
                    in patient.LabTestRecords)
                {
                    string imagePath =
                        lab.ImageResultPath?.Trim()
                        ?? "";

                    if (IsDisplayableImagePath(
                            imagePath))
                    {
                        paths.Add(imagePath);
                    }
                }
            }

            return paths.Count;
        }
        private List<object> BuildAiPatientReferences(
    string question,
    List<Cadaver> patients)
        {
            var result = new List<object>();

            List<Cadaver> matchedPatients =
                FindAiMatchedPatients(
                    question,
                    patients
                );

            foreach (Cadaver patient in matchedPatients)
            {
                string row =
                    patient.RowKey?.Trim() ?? "";

                string medicalRecordNumber =
                    patient.MedicalRecordNumber?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(row))
                {
                    continue;
                }

                string medicalRecordUrl =
                    Url.Action(
                        "MedicalRecords",
                        "Home",
                        new
                        {
                            row,
                            medicalRecordNumber
                        }
                    )
                    ?? "";

                string imageUrl =
                    Url.Action(
                        "Image",
                        "Home",
                        new
                        {
                            row
                        }
                    )
                    ?? "";

                result.Add(new
                {
                    patientRowKey = row,

                    medicalRecordNumber,

                    medicalRecordUrl,

                    imageUrl
                });
            }

            return result;
        }
        private List<object> BuildAiPatientImages(
            string question,
            List<Cadaver> patients)
        {
            var result =
                new List<object>();

            List<Cadaver> matchedPatients =
                FindAiMatchedPatients(
                    question,
                    patients
                );

            var usedPaths =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase
                );

            foreach (Cadaver patient in matchedPatients)
            {
                List<string> originalPaths =
                    patient.MedicalImagePaths
                    ?? new List<string>();

                List<string> imageIds =
                    patient.MedicalImageIds
                    ?? new List<string>();

                List<string> notes =
                    patient.MedicalImageNotes
                    ?? new List<string>();

                List<string> annotatedPaths =
                    patient.MedicalAnnotatedImagePaths
                    ?? new List<string>();

                for (
                    int i = 0;
                    i < originalPaths.Count;
                    i++)
                {
                    string originalPath =
                        originalPaths[i]?.Trim()
                        ?? "";

                    string annotatedPath =
                        i < annotatedPaths.Count
                            ? annotatedPaths[i]?.Trim()
                                ?? ""
                            : "";

                    string displayPath =
                        !string.IsNullOrWhiteSpace(
                            annotatedPath
                        )
                            ? annotatedPath
                            : originalPath;

                    if (!IsDisplayableImagePath(
                            displayPath)
                        ||
                        !usedPaths.Add(displayPath))
                    {
                        continue;
                    }

                    string imageId =
                        i < imageIds.Count
                            ? imageIds[i] ?? ""
                            : "";

                    string note =
                        i < notes.Count
                            ? notes[i] ?? ""
                            : "";

                    bool isKaggleReference =
                        note.Contains(
                            "Kaggle 教學參考影像",
                            StringComparison.OrdinalIgnoreCase
                        )
                        ||
                        originalPath.Contains(
                            "/medical-images/kaggle/",
                            StringComparison.OrdinalIgnoreCase
                        );

                    result.Add(new
                    {
                        patientRowKey =
                        patient.RowKey ?? "",

                        medicalRecordNumber =
                        patient.MedicalRecordNumber ?? "",

                        imageId,

                        mediaIndex = i,

                        imagePath =
                        displayPath,

                        originalImagePath =
                        originalPath,

                        pageUrl =
                        Url.Action(
                            "Image",
                            "Home",
                            new
                            {
                                row = patient.RowKey,
                                contentType = "medical",
                                mediaType = "image",
                                mediaIndex = i
                            }
                        ) ?? "",

                        isAnnotated =
                        !string.IsNullOrWhiteSpace(
                            annotatedPath
                        ),

                        imageType =
                        isKaggleReference
                            ? "kaggle-reference"
                            : "medical",

                        title =
                        isKaggleReference
                            ? "教學參考影像 / Kaggle Chest X-Ray / 非此病人真實影像"
                            : !string.IsNullOrWhiteSpace(
                                annotatedPath
                            )
                                ? "病歷影像（已標註）"
                                : "病歷影像",

                        note
                    });

                    // 避免一次回傳過多大型影像。
                    if (result.Count >= 12)
                    {
                        return result;
                    }
                }
                // ==============================
                // course_image:image_*
                // 課程 / 解剖影像
                // ==============================
                List<string> coursePaths =
                    patient.CourseImagePaths
                    ?? new List<string>();

                List<string> courseIds =
                    patient.CourseImageIds
                    ?? new List<string>();

                List<string> courseNotes =
                    patient.CourseImageNotes
                    ?? new List<string>();

                List<string> courseAnnotatedPaths =
                    patient.CourseAnnotatedImagePaths
                    ?? new List<string>();

                for (int i = 0; i < coursePaths.Count; i++)
                {
                    string originalCoursePath =
                        coursePaths[i]?.Trim()
                        ?? "";

                    string annotatedCoursePath =
                        i < courseAnnotatedPaths.Count
                            ? courseAnnotatedPaths[i]?.Trim() ?? ""
                            : "";

                    string displayCoursePath =
                        !string.IsNullOrWhiteSpace(
                            annotatedCoursePath
                        )
                            ? annotatedCoursePath
                            : originalCoursePath;

                    if (!IsDisplayableImagePath(
                            displayCoursePath)
                        ||
                        !usedPaths.Add(displayCoursePath))
                    {
                        continue;
                    }

                    string courseImageId =
                        i < courseIds.Count
                            ? courseIds[i] ?? ""
                            : "";

                    string courseNote =
                        i < courseNotes.Count
                            ? courseNotes[i] ?? ""
                            : "";

                    result.Add(new
                    {
                        patientRowKey =
                        patient.RowKey ?? "",

                        medicalRecordNumber =
                        patient.MedicalRecordNumber ?? "",

                        imageId =
                        courseImageId,

                        mediaIndex = i,

                        imagePath =
                        displayCoursePath,

                        originalImagePath =
                        originalCoursePath,
                        pageUrl =
    Url.Action(
        "Image",
        "Home",
        new
        {
            row = patient.RowKey,
            contentType = "course",
            mediaType = "image",
            mediaIndex = i
        }
    ) ?? "",
                        isAnnotated =
                        !string.IsNullOrWhiteSpace(
                        annotatedCoursePath
                        ),

                        imageType =
                        "course",

                        title =
                        !string.IsNullOrWhiteSpace(
                        annotatedCoursePath
                    )
                  ? "解剖影像（已標註）"
                  : "解剖影像",

                        note =
                       courseNote
                    });

                    if (result.Count >= 12)
                    {
                        return result;
                    }
                }
                // 若檢驗紀錄本身有 X 光／影像結果，也一併顯示。
                List<LabTestRecord> labRecords =
                    patient.LabTestRecords
                    ?? new List<LabTestRecord>();

                foreach (LabTestRecord lab in labRecords)
                {
                    string imagePath =
                        lab.ImageResultPath?.Trim()
                        ?? "";

                    if (!IsDisplayableImagePath(
                            imagePath)
                        ||
                        !usedPaths.Add(imagePath))
                    {
                        continue;
                    }

                    string examinationType =
                        lab.ExaminationType?.Trim()
                        ?? "";

                    bool isXray =
                        examinationType.Contains(
                            "XRay",
                            StringComparison.OrdinalIgnoreCase
                        )
                        ||
                        examinationType.Contains(
                            "X-Ray",
                            StringComparison.OrdinalIgnoreCase
                        )
                        ||
                        examinationType.Contains(
                            "X光",
                            StringComparison.OrdinalIgnoreCase
                        )
                        ||
                        examinationType.Contains(
                            "X 光",
                            StringComparison.OrdinalIgnoreCase
                        );

                    result.Add(new
                    {
                        patientRowKey =
                            patient.RowKey ?? "",

                        medicalRecordNumber =
                            patient.MedicalRecordNumber ?? "",

                        imageId =
                            lab.Id ?? "",

                        imagePath,

                        originalImagePath =
                            imagePath,

                        isAnnotated =
                            false,

                        imageType =
                            isXray
                                ? "xray"
                                : "lab",

                        title =
                            isXray
                                ? "X 光影像"
                                : "檢驗／影像結果",

                        note =
                            examinationType
                    });

                    if (result.Count >= 12)
                    {
                        return result;
                    }
                }
            }

            return result;
        }
        private List<object> BuildAiLabImages(
    Cadaver patient,
    string question)
        {
            var result =
                new List<object>();

            List<LabTestRecord> labs =
                patient.LabTestRecords
                ?? new List<LabTestRecord>();

            foreach (LabTestRecord lab in labs)
            {
                // 使用者有指定檢驗項目時，只顯示符合該問題的檢驗影像
                if (!LabMatchesQuestion(
                        lab,
                        question))
                {
                    continue;
                }

                string imagePath =
                    lab.ImageResultPath?.Trim()
                    ?? "";

                if (!IsDisplayableImagePath(
                        imagePath))
                {
                    continue;
                }

                string examinationType =
                    lab.ExaminationType?.Trim()
                    ?? "";

                bool isXray =
                    examinationType.Contains(
                        "XRay",
                        StringComparison.OrdinalIgnoreCase
                    )
                    ||
                    examinationType.Contains(
                        "X-Ray",
                        StringComparison.OrdinalIgnoreCase
                    )
                    ||
                    examinationType.Contains(
                        "X光",
                        StringComparison.OrdinalIgnoreCase
                    )
                    ||
                    examinationType.Contains(
                        "X 光",
                        StringComparison.OrdinalIgnoreCase
                    );

                result.Add(new
                {
                    patientRowKey =
                        patient.RowKey ?? "",

                    medicalRecordNumber =
                        patient.MedicalRecordNumber ?? "",

                    imageId =
                        lab.Id ?? "",

                    imagePath,

                    originalImagePath =
                        imagePath,

                    isAnnotated =
                        false,

                    imageType =
                        isXray
                            ? "xray"
                            : "lab",

                    title =
                        isXray
                            ? "X 光影像"
                            : "檢驗影像",

                    note =
                        !string.IsNullOrWhiteSpace(
                            examinationType)
                            ? examinationType
                            : GetLabDisplayName(lab)
                });
            }

            return result;
        }
        private List<object> BuildAiExaminationImages(
            Cadaver patient,
            string examinationType)
        {
            var result =
                new List<object>();

            List<LabTestRecord> labs =
                patient.LabTestRecords
                ?? new List<LabTestRecord>();

            foreach (LabTestRecord lab in labs)
            {
                string imagePath =
                    lab.ImageResultPath?.Trim()
                    ?? "";

                if (!IsDisplayableImagePath(
                        imagePath))
                {
                    continue;
                }

                string searchText =
                    string.Join(
                        " ",
                        lab.ExaminationType ?? "",
                        lab.TestItem ?? "",
                        lab.TestItemName ?? "",
                        lab.TestItemCode ?? "",
                        lab.BodyPart ?? "",
                        lab.Interpretation ?? ""
                    );

                if (!MatchesExaminationType(
                        searchText,
                        examinationType))
                {
                    continue;
                }

                string displayName =
                    GetExaminationDisplayName(
                        examinationType
                    );

                result.Add(new
                {
                    patientRowKey =
                        patient.RowKey ?? "",

                    medicalRecordNumber =
                        patient.MedicalRecordNumber ?? "",

                    imageId =
                        lab.Id ?? "",

                    imagePath,

                    originalImagePath =
                        imagePath,

                    pageUrl =
                        imagePath,

                    isAnnotated =
                        false,

                    imageType =
                        examinationType,

                    title =
                        displayName,

                    note =
                        !string.IsNullOrWhiteSpace(
                            lab.ExaminationType)
                            ? lab.ExaminationType
                            : GetLabDisplayName(lab)
                });

                if (result.Count >= 12)
                {
                    break;
                }
            }

            return result;
        }
        private bool MatchesExaminationType(
    string text,
    string examinationType)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return examinationType switch
            {
                "xray" =>
                    ContainsAny(
                        text,
                        "XRay",
                        "X-Ray",
                        "X光",
                        "X 光"
                    ),

                "ct" =>
                    ContainsAny(
                        text,
                        "CT",
                        "Computed Tomography",
                        "電腦斷層"
                    ),

                "mri" =>
                    ContainsAny(
                        text,
                        "MRI",
                        "Magnetic Resonance",
                        "磁振造影",
                        "核磁共振",
                        "磁共振"
                    ),

                "ultrasound" =>
                    ContainsAny(
                        text,
                        "Ultrasound",
                        "超音波",
                        "超聲波"
                    ),

                "endoscopy" =>
                    ContainsAny(
                        text,
                        "Endoscopy",
                        "內視鏡"
                    ),

                "pathology" =>
                    ContainsAny(
                        text,
                        "Pathology",
                        "Histology",
                        "病理",
                        "切片",
                        "組織切片"
                    ),

                "microscopy" =>
                    ContainsAny(
                        text,
                        "Microscopy",
                        "顯微",
                        "顯微鏡"
                    ),

                "pet" =>
                    ContainsAny(
                        text,
                        "PET",
                        "正子"
                    ),

                "mammography" =>
                    ContainsAny(
                        text,
                        "Mammography",
                        "乳房攝影"
                    ),

                "angiography" =>
                    ContainsAny(
                        text,
                        "Angiography",
                        "血管攝影"
                    ),

                _ =>
                    false
            };
        }
        private string GetExaminationDisplayName(
            string examinationType)
        {
            return examinationType
                .Trim()
                .ToLowerInvariant() switch
            {
                "xray" => "X 光",
                "ct" => "CT 電腦斷層",
                "mri" => "MRI 磁振造影",
                "ultrasound" => "超音波",
                "endoscopy" => "內視鏡",
                "pathology" => "病理切片",
                "microscopy" => "顯微鏡",
                "pet" => "PET 正子斷層",
                "mammography" => "乳房攝影",
                "angiography" => "血管攝影",
                _ => "檢查"
            };
        }
        private List<object> BuildAiPatientVideos(
            Cadaver patient,
            string question)
        {
            var result =
                new List<object>();

            var usedPaths =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase
                );

            bool asksMedical =
                ContainsAny(
                    question,
                    "病歷影片",
                    "醫療影片",
                    "medical"
                );

            bool asksCourse =
                ContainsAny(
                    question,
                    "解剖影片",
                    "課程影片",
                    "教學影片",
                    "course"
                );

            string examinationType =
                ExtractExaminationType(
                    question
                );
            Console.WriteLine(
    $"[VIDEO DEBUG] " +
    $"Question={question}, " +
    $"ExaminationType={examinationType}, " +
    $"LabCount={patient.LabTestRecords?.Count ?? 0}"
);
            bool asksSpecificExamination =
                !string.IsNullOrWhiteSpace(
                    examinationType
                );


            // ============================================================
            // 一般 course / medical 影片
            // ============================================================

            // 如果問的是「內視鏡影片 / 超音波影片 / CT 影片」等
            // 就不要混入一般 course / medical 影片。
            if (!asksSpecificExamination)
            {
                List<string> videoPaths =
                    patient.VideoPaths
                    ?? new List<string>();

                List<string> videoTypes =
                    patient.VideoTypes
                    ?? new List<string>();

                List<string> videoNotes =
                    patient.VideoNotes
                    ?? new List<string>();

                List<string> videoCategories =
                    patient.VideoCategories
                    ?? new List<string>();

                int medicalIndex = 0;
                int courseIndex = 0;

                for (int i = 0;
                     i < videoPaths.Count;
                     i++)
                {
                    string videoPath =
                        videoPaths[i]?.Trim()
                        ?? "";

                    string contentType =
                        i < videoTypes.Count
                            ? videoTypes[i]?.Trim()
                                .ToLowerInvariant()
                                ?? "course"
                            : "course";

                    if (
                        contentType != "medical"
                        &&
                        contentType != "course"
                    )
                    {
                        contentType =
                            "course";
                    }

                    int mediaIndex;

                    if (contentType == "medical")
                    {
                        mediaIndex =
                            medicalIndex;

                        medicalIndex++;
                    }
                    else
                    {
                        mediaIndex =
                            courseIndex;

                        courseIndex++;
                    }

                    if (string.IsNullOrWhiteSpace(
                            videoPath))
                    {
                        continue;
                    }
                    if (
                        asksMedical
                        &&
                        contentType != "medical"
                    )
                    {
                        continue;
                    }

                    if (
                        asksCourse
                        &&
                        contentType != "course"
                    )
                    {
                        continue;
                    }

                    if (!usedPaths.Add(
                            videoPath))
                    {
                        continue;
                    }
                    string note =
                        i < videoNotes.Count
                            ? videoNotes[i] ?? ""
                            : "";

                    string category =
                        i < videoCategories.Count
                            ? videoCategories[i] ?? ""
                            : "";

                    string pageUrl =
                        Url.Action(
                            "Image",
                            "Home",
                            new
                            {
                                row =
                                    patient.RowKey,

                                contentType,

                                mediaType =
                                    "video",

                                mediaIndex
                            }
                        )
                        ?? "";

                    result.Add(new
                    {
                        patientRowKey =
                            patient.RowKey ?? "",

                        medicalRecordNumber =
                            patient.MedicalRecordNumber
                            ?? "",

                        videoPath,

                        pageUrl,

                        contentType,

                        mediaIndex,

                        title =
                            string.IsNullOrWhiteSpace(
                                category)
                                ? contentType == "medical"
                                    ? "病歷影片"
                                    : "課程影片"
                                : category,

                        note,

                        source =
                            "patient"
                    });

                    if (result.Count >= 12)
                    {
                        return result;
                    }
                }
            }


            // ============================================================
            // Lab / Examination 影片
            // ============================================================

            // 明確問「病歷影片 / 課程影片」時，
            // 不混入 Lab 影片。
            if (!asksMedical &&
                !asksCourse)
            {
                List<LabTestRecord> labs =
                    patient.LabTestRecords
                    ?? new List<LabTestRecord>();

                foreach (
                    LabTestRecord lab
                    in labs)
                {
                    Console.WriteLine(
                        $"[VIDEO DEBUG LAB] " +
                        $"Id={lab.Id}, " +
                        $"Exam={lab.ExaminationType}, " +
                        $"TestItem={lab.TestItem}, " +
                        $"Video={lab.VideoResultPath}"
                    );

                    string videoPath =
                        lab.VideoResultPath?.Trim()
                        ?? "";

                    if (string.IsNullOrWhiteSpace(
                            videoPath))
                    {
                        continue;
                    }

                    string searchText =
                        string.Join(
                            " ",
                            lab.ExaminationType ?? "",
                            lab.TestItem ?? "",
                            lab.TestItemName ?? "",
                            lab.TestItemCode ?? "",
                            lab.BodyPart ?? "",
                            lab.Interpretation ?? ""
                        );

                    // 有指定內視鏡、超音波、CT...
                    // 就只保留該檢查類型。

                    if (
                        asksSpecificExamination
                        &&
                        !MatchesExaminationType(
                            searchText,
                            examinationType
                        )
                    )
                    {
                        continue;
                    }

                    if (!usedPaths.Add(
                            videoPath))
                    {
                        continue;
                    }

                    string displayName;

                    if (asksSpecificExamination)
                    {
                        displayName =
                            GetExaminationDisplayName(
                                examinationType
                            );
                    }
                    else
                    {
                        string detectedType =
                            ExtractExaminationType(
                                string.Join(
                                    " ",
                                    lab.ExaminationType ?? "",
                                    lab.TestItem ?? "",
                                    lab.TestItemName ?? ""
                                )
                            );

                        displayName =
                            !string.IsNullOrWhiteSpace(detectedType)
                                ? GetExaminationDisplayName(
                                    detectedType
                                )
                                : !string.IsNullOrWhiteSpace(
                                    lab.ExaminationType)
                                    ? lab.ExaminationType
                                    : GetLabDisplayName(lab);
                    }

                    string pageUrl =
                        Url.Action(
                            "Image",
                            "Home",
                            new
                            {
                                row =
                                    patient.RowKey,

                                contentType =
                                    "lab",

                                mediaType =
                                    "video",

                                labRecordId =
                                    lab.Id
                            }
                        )
                        ?? "";

                    result.Add(new
                    {
                        patientRowKey =
                            patient.RowKey ?? "",

                        medicalRecordNumber =
                            patient.MedicalRecordNumber
                            ?? "",

                        videoPath,

                        pageUrl,

                        contentType =
                            "lab",

                        mediaIndex =
                            0,

                        labRecordId =
                            lab.Id ?? "",

                        title =
                            $"{displayName}影片",

                        note =
                            !string.IsNullOrWhiteSpace(
                                lab.Interpretation)
                                ? lab.Interpretation
                                : GetLabDisplayName(lab),

                        source =
                            "lab"
                    });

                    if (result.Count >= 12)
                    {
                        return result;
                    }
                }
            }

            return result;
        }
        private async Task<string> BuildMedicalRecordContext(
            string question)
        {
            if (string.IsNullOrWhiteSpace(question))
            {
                return "本次沒有有效的病歷搜尋問題。";
            }

            List<Cadaver> patients =
                await GetAllData();
            if (patients == null ||
                patients.Count == 0)
            {
                return "目前 HBase 中沒有可供查詢的病歷資料。";
            }

            question = question.Trim();

            List<string> keywords =
                ExtractSearchKeywords(question);

            // 先做完整病人識別碼比對。
            // 例如查 P002 時，不會再因為字串中含有 "2"
            // 而誤抓 RowKey = "2"。
            string exactPatientId =
                ExtractExactPatientId(
                    question,
                    patients
                );

            List<Cadaver> exactMatchedPatients =
                string.IsNullOrWhiteSpace(
                    exactPatientId
                )
                    ? new List<Cadaver>()
                    : patients
                        .Where(patient =>
                            IsSamePatientIdentifier(
                                patient,
                                exactPatientId
                            )
                        )
                        .ToList();
            bool isAllPatientQuery =
    question.Contains("哪些病人", StringComparison.OrdinalIgnoreCase)
    ||
    question.Contains("所有病人", StringComparison.OrdinalIgnoreCase)
    ||
    question.Contains("列出所有", StringComparison.OrdinalIgnoreCase)
    ||
    question.Contains("全部病人", StringComparison.OrdinalIgnoreCase)
    ||
    question.Contains("哪些人的", StringComparison.OrdinalIgnoreCase);
            bool isHypotheticalRangeQuery =
                question.Contains(
                    "如果",
                    StringComparison.OrdinalIgnoreCase
                )
                ||
                question.Contains(
                    "假設",
                    StringComparison.OrdinalIgnoreCase
                )
                ||
                question.Contains(
                    "假如",
                    StringComparison.OrdinalIgnoreCase
                ); List<Cadaver> selectedPatients;
            if (exactMatchedPatients.Count > 0)
            {
                // 明確指定病人
                selectedPatients =
                    exactMatchedPatients
                        .Take(3)
                        .ToList();
            }
            else if (isAllPatientQuery)
            {
                // 跨病人、全體資料查詢
                selectedPatients = patients.ToList();
            }
            else if (isHypotheticalRangeQuery)
            {
                // 假設性範圍問題：提供具有可用參考範圍的病人資料
                selectedPatients =
                    patients
                        .Where(patient =>
                            patient.LabTestRecords != null
                            &&
                            patient.LabTestRecords.Any(record =>
                                !string.IsNullOrWhiteSpace(
                                    record.ReferenceRange
                                )
                            )
                        )
                        .ToList();
            }
            else
            {
                // 一般模糊查詢
                selectedPatients =
                    patients
                        .Select(patient => new
                        {
                            Patient = patient,
                            Score = CalculatePatientMatchScore(
                                patient,
                                question,
                                keywords
                            )
                        })
                        .Where(x => x.Score > 0)
                        .OrderByDescending(x => x.Score)
                        .Take(5)
                        .Select(x => x.Patient)
                        .ToList();
            }
            if (selectedPatients.Count == 0)
            {
                return
                    "HBase 病歷中找不到與使用者問題相關的紀錄。";
            }

            var builder =
                new StringBuilder();

            builder.AppendLine(
                "以下是系統從 HBase 檢索到的相關病歷。"
            );

            builder.AppendLine(
                "模型只能根據這些內容回答特定病人的問題。"
            );

            builder.AppendLine(
                "未出現在資料中的內容不得自行推測。"
            );

            builder.AppendLine();

            int patientIndex = 1;

            foreach (Cadaver patient in selectedPatients)
            {
                builder.AppendLine(
                    $"病人編號：{patient.RowKey}"
                );

                builder.AppendLine(
                    $"病歷號碼：{patient.MedicalRecordNumber}"
                );

                builder.AppendLine(
                    "以下病人基本資料為 HBase 目前最新值。"
                );

                builder.AppendLine(
                    "若與歷史異動紀錄衝突，必須以目前值為準。"
                );

                // 刻意不傳姓名、身分證、電話與地址
                builder.AppendLine(
                    $"性別：{patient.Gender}"
                );

                builder.AppendLine(
                    $"年齡：{patient.Age}"
                );

                builder.AppendLine(
                    $"血型：{patient.BloodType}"
                );

                builder.AppendLine(
                    $"藥物過敏史：{RedactSensitiveText(patient.DrugAllergyHistory)}"
                );

                builder.AppendLine(
                    $"過去病史：{RedactSensitiveText(patient.PastMedicalHistory)}"
                );

                builder.AppendLine(
                    $"住院原因：{patient.AdmissionReason}"
                );

                builder.AppendLine(
                    $"入院診斷碼：{patient.AdmissionDiagnosisCode}"
                );

                builder.AppendLine(
                    $"主診斷碼：{patient.PrimaryDiagnosisCode}"
                );

                builder.AppendLine(
                    $"次診斷碼：{patient.SecondaryDiagnosisCode}"
                );

                builder.AppendLine(
                    $"出院診斷碼：{patient.DischargeDiagnosisCode}"
                );

                builder.AppendLine(
                    $"出院狀態：{patient.DischargeStatus}"
                );

                builder.AppendLine(
                    $"住院天數：{patient.LengthOfStay}"
                );

                builder.AppendLine(
                    $"加護病房天數：{patient.IcuDays}"
                );

                builder.AppendLine(
                    $"解剖過程：{RedactSensitiveText(patient.DissectionProcessRecord)}"
                );

                builder.AppendLine(
                    $"教師講解：{RedactSensitiveText(patient.TeacherExplanationRecord)}"
                );

                builder.AppendLine(
                    $"病理發現：{RedactSensitiveText(patient.PathologicalFindings)}"
                );

                builder.AppendLine(
                    $"教學註記：{RedactSensitiveText(patient.TeachingNote)}"
                );
                List<MedicalRecord> allRecords =
                    patient.MedicalRecords
                    ?? new List<MedicalRecord>();

                List<MedicalRecord> clinicalRecords =
                    allRecords
                        .Where(record =>
                            !string.Equals(
                                record.Diagnosis,
                                "PatientCreated",
                                StringComparison.OrdinalIgnoreCase
                            )
                            &&
                            !string.Equals(
                                record.Diagnosis,
                                "PatientUpdated",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .ToList();

                List<MedicalRecord> auditRecords =
                    allRecords
                        .Where(record =>
                            string.Equals(
                                record.Diagnosis,
                                "PatientCreated",
                                StringComparison.OrdinalIgnoreCase
                            )
                            ||
                            string.Equals(
                                record.Diagnosis,
                                "PatientUpdated",
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        .ToList();

                builder.AppendLine();

                if (clinicalRecords.Count > 0)
                {
                    builder.AppendLine("臨床病歷紀錄：");

                    foreach (
                        MedicalRecord record
                        in clinicalRecords.Take(10))
                    {
                        builder.AppendLine(
                            $"日期：{record.OccurredAt}"
                        );

                        builder.AppendLine(
                            $"診斷：{RedactSensitiveText(record.Diagnosis)}"
                        );

                        builder.AppendLine(
                            $"部位：{RedactSensitiveText(record.BodyPart)}"
                        );

                        builder.AppendLine(
                            $"檢查發現：{RedactSensitiveText(record.Finding)}"
                        );

                        builder.AppendLine(
                            $"處置或治療：{RedactSensitiveText(record.Treatment)}"
                        );

                        builder.AppendLine(
                            $"醫師或紀錄者：{RedactSensitiveText(record.Physician)}"
                        );

                        builder.AppendLine(
                            $"備註：{RedactSensitiveText(record.Note)}"
                        );

                        builder.AppendLine("---");
                    }
                }
                else
                {
                    builder.AppendLine(
                        "臨床病歷紀錄：無"
                    );
                }
                if (auditRecords.Count > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine(
                        $"基本資料異動紀錄：共 {auditRecords.Count} 筆"
                    );

                    builder.AppendLine(
                        "基本資料異動紀錄僅用於稽核，不代表臨床診斷。"
                    );

                    foreach (
                        MedicalRecord audit in auditRecords
                            .OrderByDescending(record =>
                                DateTime.TryParse(
                                    record.OccurredAt,
                                    out DateTime date
                                )
                                    ? date
                                    : DateTime.MinValue
                            )
                    )
                    {
                        builder.AppendLine(
                            $"異動時間：{audit.OccurredAt}"
                        );

                        builder.AppendLine(
                            $"異動類型：{audit.Diagnosis}"
                        );

                        builder.AppendLine(
                            $"異動項目：{audit.BodyPart}"
                        );

                        builder.AppendLine(
                            $"異動內容：{RedactSensitiveText(audit.Finding)}"
                        );

                        builder.AppendLine(
                            $"異動說明：{RedactSensitiveText(audit.Note)}"
                        );

                        builder.AppendLine(
                            $"執行者：{audit.Physician}"
                        );

                        builder.AppendLine("---");
                    }
                }
                else
                {
                    builder.AppendLine(
                        "基本資料異動紀錄：無"
                    );
                }

                List<LabTestRecord> labRecords =
                    patient.LabTestRecords
                    ?? new List<LabTestRecord>();

                if (labRecords.Count > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine("檢驗紀錄：");

                    foreach (
                        LabTestRecord lab
                        in labRecords.Take(10))
                    {
                        builder.AppendLine(
                            $"日期：{lab.TestDate}"
                        );

                        builder.AppendLine(
                            $"檢驗類型：{RedactSensitiveText(lab.TestItem)}"
                        );

                        builder.AppendLine(
                            $"檢驗代碼：{lab.TestItemCode}"
                        );

                        builder.AppendLine(
                            $"檢驗項目名稱：{RedactSensitiveText(lab.TestItemName)}"
                        );

                        builder.AppendLine(
                            $"檢驗結果：{RedactSensitiveText(lab.TestResult)}"
                        );

                        builder.AppendLine(
                            $"數值結果：{lab.NumericResult} {lab.Unit}"
                        );

                        builder.AppendLine(
                            $"文字結果：{RedactSensitiveText(lab.TextResult)}"
                        );

                        builder.AppendLine(
                            $"參考範圍：{lab.ReferenceRange}"
                        );

                        builder.AppendLine(
                            $"異常標記：{lab.AbnormalFlag}"
                        );

                        builder.AppendLine(
                            $"檢驗方法：{RedactSensitiveText(lab.TestMethod)}"
                        );

                        builder.AppendLine("---");
                    }
                }
                else
                {
                    builder.AppendLine(
                        "檢驗紀錄：無"
                    );
                }
                builder.AppendLine();
                patientIndex++;
            }

            string context =
                builder.ToString();

            const int maxContextLength =
                14000;

            if (context.Length >
                maxContextLength)
            {
                context =
                    context[..maxContextLength] +
                    "\n病歷內容因輸入長度限制已截斷。";
            }

            return context;
        }
        // =========================
        // AI 病理助教
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AskAi(
            [FromBody] AiChatRequest request,
            CancellationToken cancellationToken)
        {
            // 只允許已登入使用者使用 AI
            if (!IsLogin())
            {
                return Unauthorized(new
                {
                    error = "登入狀態已失效，請重新登入。"
                });
            }

            if (IsAdmin())
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new
                    {
                        error = "管理員不提供 AI 助教功能。"
                    }
                );
            }

            if (request == null ||
                request.Messages == null ||
                request.Messages.Count == 0)
            {
                return BadRequest(new
                {
                    error = "請輸入問題。"
                });
            }

            var allowedMessages = request.Messages
                .Where(x =>
                    x != null &&
                    !string.IsNullOrWhiteSpace(x.Content) &&
                    (
                        string.Equals(
                            x.Role,
                            "user",
                            StringComparison.OrdinalIgnoreCase
                        )
                        ||
                        string.Equals(
                            x.Role,
                            "assistant",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                .TakeLast(8)
                .Select(x => new
                {
                    role =
                        string.Equals(
                            x.Role,
                            "assistant",
                            StringComparison.OrdinalIgnoreCase
                        )
                            ? "assistant"
                            : "user",

                    content =
                        x.Content.Trim().Length > 3000
                            ? x.Content.Trim()[..3000]
                            : x.Content.Trim()
                })
                .ToList();
            if (allowedMessages.Count == 0)
            {
                return BadRequest(new
                {
                    error = "沒有有效的問題內容。"
                });
            }
            int totalCharacters =
                allowedMessages.Sum(
                    x => x.content.Length
                );

            if (totalCharacters > 12000)
            {
                return BadRequest(new
                {
                    error =
                        "對話內容過長，請清除對話後重新提問。"
                });
            }

            // 取得本次最新的使用者問題
            string latestQuestion =
                allowedMessages
                    .LastOrDefault(x =>
                        string.Equals(
                            x.role,
                            "user",
                            StringComparison.OrdinalIgnoreCase
                        ))
                    ?.content
                ?? "";

            if (string.IsNullOrWhiteSpace(
                    latestQuestion))
            {
                return BadRequest(new
                {
                    error =
                        "找不到有效的使用者問題。"
                });
            }
            List<Cadaver> allPatients =
    await GetAllData();

            var conversationMessages =
                allowedMessages
                    .Select(x =>
                        (
                            Role: x.role,
                            Content: x.content
                        )
                    )
                    .ToList();

            string effectiveQuestion =
                latestQuestion;

            bool latestHasMediaType =
                ContainsAny(
                    latestQuestion,
                    "影片",
                    "視頻",
                    "video",
                    "影像",
                    "圖片",
                    "照片"
                );

            if (!latestHasMediaType)
            {
                for (int i = conversationMessages.Count - 1; i >= 0; i--)
                {
                    if (!string.Equals(
                            conversationMessages[i].Role,
                            "user",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string previousQuestion =
                        conversationMessages[i].Content;

                    if (string.Equals(
                            previousQuestion,
                            latestQuestion,
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (ContainsAny(
                            previousQuestion,
                            "影片",
                            "視頻",
                            "video"))
                    {
                        effectiveQuestion =
                            latestQuestion + " 影片";

                        break;
                    }

                    if (ContainsAny(
                            previousQuestion,
                            "影像",
                            "圖片",
                            "照片"))
                    {
                        effectiveQuestion =
                            latestQuestion + " 影像";

                        break;
                    }
                }
            }

            string inheritedPatientId =
                ResolvePreviousPatientId(
                    effectiveQuestion,
                    allPatients,
                    conversationMessages
                );

            string contextualQuestion =
                BuildContextualQuestion(
                    effectiveQuestion,
                    inheritedPatientId
                );
            // ====================================================
            // Intent Router
            // ====================================================
            bool isSuggestedTopic =
    request.IsSuggestedTopic;

            AiIntentResult intent =
                isSuggestedTopic
                    ? new AiIntentResult
                    {
                        Intent = "unknown"
                    }
                    : ParseAiIntent(
                        contextualQuestion,
                        allPatients
                    );
            // ====================================================
            // 沒指定病人，只問「肺部X光片 / 肺部超音波」
            // 直接從 medical_media 回傳圖片
            // 不跑病歷搜尋、不顯示 patientReferences
            // ====================================================
            if (intent.Intent ==
                "examination_image_query")
            {
                List<object> images =
                    await BuildAiHBaseGlobalExaminationImages(
                        intent.ExaminationType
                    );

                string displayName =
                    GetExaminationDisplayName(
                        intent.ExaminationType
                    );

                if (images.Count == 0)
                {
                    return Ok(new
                    {
                        answer =
                            $"目前沒有可顯示的 {displayName} 影像。",

                        images,

                        videos =
                            new List<object>(),

                        patientReferences =
                            new List<object>()
                    });
                }

                return Ok(new
                {
                    answer =
                        $"已找到 {images.Count} 張 {displayName} 影像，顯示如下。",

                    images,

                    videos =
                        new List<object>(),

                    patientReferences =
                        new List<object>()
                });
            }

            if (intent.Intent ==
                "list_patients_with_images")
            {
                var patientsWithImages =
                    allPatients
                        .Where(
                            PatientHasAnyImage
                        )
                        .Select(patient => new
                        {
                            Patient = patient,
                            Count =
                                CountPatientDisplayImages(
                                    patient
                                )
                        })
                        .Where(x =>
                            x.Count > 0
                        )
                        .OrderBy(x =>
                            x.Patient.RowKey
                        )
                        .ToList();

                if (patientsWithImages.Count == 0)
                {
                    return Ok(new
                    {
                        answer =
                            "目前沒有任何病人具有可顯示的病歷影像或 X 光影像。",
                        images =
                            new List<object>()
                    });
                }

                string patientList =
                    string.Join(
                        "、",
                        patientsWithImages.Select(x =>
                            $"{x.Patient.RowKey}（{x.Count} 張）"
                        )
                    );

                return Ok(new
                {
                    answer =
                        $"目前有影像的病人共有 {patientsWithImages.Count} 位：{patientList}。",
                    images =
                        new List<object>()
                });
            }
            if (intent.Intent ==
    "patient_video_query")
            {
                Cadaver? patient =
                    FindPatientByIdentifier(
                        intent.PatientId,
                        allPatients
                    );

                if (patient == null)
                {
                    return Ok(new
                    {
                        answer =
                            $"找不到病人編號或病歷號碼 {intent.PatientId}。",

                        images =
                            new List<object>(),

                        videos =
                            new List<object>()
                    });
                }

                List<object> videos =
                    BuildAiPatientVideos(
                        patient,
                        contextualQuestion
                    );

                if (videos.Count == 0)
                {
                    return Ok(new
                    {
                        answer =
                            $"病人編號 {patient.RowKey} 目前沒有可顯示的影片。",

                        images =
                            new List<object>(),

                        videos
                    });
                }

                return Ok(new
                {
                    answer =
                        $"已找到病人編號 {patient.RowKey} 的 {videos.Count} 部相關影片。",

                    images =
                        new List<object>(),

                    videos
                });
            }
            if (intent.Intent ==
    "patient_examination_image_query")
            {
                Cadaver? patient =
                    FindPatientByIdentifier(
                        intent.PatientId,
                        allPatients
                    );

                if (patient == null)
                {
                    return Ok(new
                    {
                        answer =
                            $"找不到病人編號或病歷號碼 {intent.PatientId}。",

                        images =
                            new List<object>()
                    });
                }

                List<object> images =
                    await BuildAiHBaseExaminationImages(
                        patient,
                        intent.ExaminationType
                    );

                // 相容既有 lab_test:image_result_path。
                // 新的肺部 X 光／超音波會優先從 medical_media 讀取。
                images.AddRange(
                    BuildAiExaminationImages(
                        patient,
                        intent.ExaminationType
                    )
                );

                images =
                    images
                        .Take(12)
                        .ToList();

                string displayName =
                    GetExaminationDisplayName(
                        intent.ExaminationType
                    );

                if (images.Count == 0)
                {
                    return Ok(new
                    {
                        answer =
                            $"病人編號 {patient.RowKey} 目前沒有可顯示的 {displayName}。",

                        images
                    });
                }

                return Ok(new
                {
                    answer =
                        $"已找到病人編號 {patient.RowKey} 的 {images.Count} 張 {displayName}，顯示如下。",

                    images
                });
            }
            if (intent.Intent ==
                "patient_image_query")
            {
                Cadaver? patient =
                    FindPatientByIdentifier(
                        intent.PatientId,
                        allPatients
                    );

                if (patient == null)
                {
                    return Ok(new
                    {
                        answer =
                            $"找不到病人編號或病歷號碼 {intent.PatientId}。",
                        images =
                            new List<object>()
                    });
                }

                List<object> images =
                    await BuildAiAllHBasePatientImages(
                        patient
                    );

                images.AddRange(
                    BuildAiPatientImages(
                        contextualQuestion,
                        new List<Cadaver>
                        {
                            patient
                        }
                    )
                );

                images =
                    images
                        .Take(12)
                        .ToList();

                if (images.Count == 0)
                {
                    return Ok(new
                    {
                        answer =
                                     $"病人編號 {patient.RowKey} 目前沒有可顯示的相關影像。",
                        images
                    });
                }

                return Ok(new
                {
                    answer =
                        $"已找到病人編號 {patient.RowKey} 的 {images.Count} 張相關影像，顯示如下。",
                    images
                });
            }

            if (intent.Intent ==
                "patient_filter")
            {
                List<Cadaver> matchedPatients =
                    FilterPatientsByIntent(
                        intent,
                        contextualQuestion,
                        allPatients
                    );

                // 跨病人條件搜尋全部由 C# 直接回答，
                // 不再送到 Ollama，避免疾病查詢長時間卡住。
                if (matchedPatients.Count == 0)
                {
                    return Ok(new
                    {
                        answer =
                            "目前 HBase 病歷中沒有符合條件的病人。",

                        images =
                            new List<object>(),

                        videos =
                            new List<object>(),

                        patientReferences =
                            new List<object>()
                    });
                }

                // 單純年齡／性別查詢沿用原本格式。
                if (intent.MinAge.HasValue ||
                    intent.MaxAge.HasValue ||
                    !string.IsNullOrWhiteSpace(
                        intent.Gender))
                {
                    return Ok(new
                    {
                        answer =
                            BuildPatientFilterAnswer(
                                intent,
                                matchedPatients
                            ),

                        images =
                            new List<object>(),

                        videos =
                            new List<object>(),

                        patientReferences =
                            BuildAiPatientReferences(
                                contextualQuestion,
                                matchedPatients
                            )
                    });
                }

                string patientList =
                    string.Join(
                        "\n",
                        matchedPatients
                            .Take(30)
                            .Select(patient =>
                            {
                                var details =
                                    new List<string>();

                                if (!string.IsNullOrWhiteSpace(
                                        patient.Gender))
                                {
                                    details.Add(
                                        $"性別={patient.Gender}"
                                    );
                                }

                                if (!string.IsNullOrWhiteSpace(
                                        patient.Age))
                                {
                                    details.Add(
                                        $"年齡={patient.Age}"
                                    );
                                }

                                if (!string.IsNullOrWhiteSpace(
                                        patient.PrimaryDiagnosisCode))
                                {
                                    details.Add(
                                        $"主診斷碼={patient.PrimaryDiagnosisCode}"
                                    );
                                }

                                if (!string.IsNullOrWhiteSpace(
                                        patient.PastMedicalHistory))
                                {
                                    details.Add(
                                        $"過去病史={RedactSensitiveText(patient.PastMedicalHistory)}"
                                    );
                                }

                                string detailText =
                                    details.Count > 0
                                        ? "（" +
                                          string.Join(
                                              "；",
                                              details
                                          ) +
                                          "）"
                                        : "";

                                return
                                    $"{patient.RowKey}{detailText}";
                            })
                    );

                string keywordText =
                    !string.IsNullOrWhiteSpace(
                        intent.Keyword)
                        ? $"「{intent.Keyword}」"
                        : "目前條件";

                return Ok(new
                {
                    answer =
                        $"目前找到 {matchedPatients.Count} 筆與 {keywordText} 相關的病歷：" +
                        $"\n{patientList}",

                    images =
                        new List<object>(),

                    videos =
                        new List<object>(),

                    patientReferences =
                        BuildAiPatientReferences(
                            contextualQuestion,
                            matchedPatients
                        )
                });
            }

            // patient_detail 與 lab_query 仍會交給 Ollama 組織語句，
            // 但 BuildMedicalRecordContext 已改成完整病人 ID 精準比對，
            // 所以 P002 不會再誤抓病人 2。
            IActionResult? labQueryResult =
                TryHandleLabQuery(
                    contextualQuestion,
                    intent,
                    allPatients
                );

            if (labQueryResult != null)
            {
                return labQueryResult;
            }

            IActionResult? patientDetailResult =
                TryHandlePatientDetailQuery(
                    contextualQuestion,
                    intent,
                    allPatients
                );

            if (patientDetailResult != null)
            {
                return patientDetailResult;
            }

            string? deterministicAnswer =
                TryAnswerHypotheticalRangeQuestion(
                    contextualQuestion,
                    allPatients
                );

            if (!string.IsNullOrWhiteSpace(
                    deterministicAnswer
                ))
            {
                return Ok(new
                {
                    answer =
                        deterministicAnswer,

                    images =
                        new List<object>(),

                    videos =
                        new List<object>()
                });
            }
            // 根據最新問題搜尋 HBase 病歷
            string medicalRecordContext;

            try
            {
                medicalRecordContext =
                    isSuggestedTopic
                        ? "這是一般醫學教學問題，請直接回答問題，不要列出病人、病歷號碼或病歷資料。"
                        : await BuildMedicalRecordContext(
                            contextualQuestion
                        );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "AI 搜尋 HBase 病歷時發生錯誤。"
                );

                medicalRecordContext =
                    "目前無法讀取 HBase 病歷資料。";
            }

            var ollamaMessages =
                new List<object>
                {
        new
        {
            role = "system",

            content = $"""
                你是病理教學系統中的 AI 助教。

                你可以使用系統從 HBase 檢索出的病歷資料回答問題。

                回答規則：
                0. 系統已在模型前方完成查詢意圖判斷與 HBase 資料檢索。不得自行改變病人識別碼、重新猜測病人或加入未被檢索出的病人。
                1. 一律使用繁體中文。
                2. 優先直接回答使用者的問題。
                3. 若問題涉及特定病人，只能依據下方提供的 HBase 病歷回答。
                4. 不可自行捏造診斷、疾病、檢驗值、病理結果、治療紀錄或日期。
                5. 如果提供的病歷資料沒有答案，必須明確回答「目前提供的病歷資料中查無相關資訊」。
                6. 如果有多位病人符合條件，應依病人編號分別說明。
                7. 回答檢驗結果時，必須保留原始數值、單位、參考範圍與異常標記。
                8. 不可將一般醫學知識當成特定病人的病歷事實。
                9. 若需要補充一般醫學知識，必須清楚標示為一般醫學說明。
                10. 不要輸出姓名、身分證字號、電話或地址。
                11. 不要輸出思考過程、內部分析、作答規劃或自我對話。
                12. 不要使用星號、井字號、反引號或 Markdown 特殊符號。
                13. 使用自然、清楚且適合教學的方式回答。
                14. 回答內容僅供醫學教育使用，不可取代正式醫療診斷。
                15. 病人目前基本資料代表 HBase 中的最新狀態。
                16. 如果目前基本資料與歷史異動紀錄衝突，必須以目前基本資料為準。
                17. PatientCreated 與 PatientUpdated 是系統稽核紀錄，不是臨床診斷。
                18. 基本資料異動紀錄不可當作診斷、檢查發現或治療紀錄。
                19. 不得自行評論資料是否合理、是否符合一般病患情境，除非使用者明確要求進行資料品質分析。
                20. 不得輸出已被標示為「已隱藏」的個人資料。
                21. 回答檢驗紀錄時，日期應使用「於某日進行檢驗」或「最近一次檢驗日期為某日」等自然表述，不得使用「行為檢驗」。
                22. 只要已找到一筆以上符合問題的資料，就不得再回答「查無相關資訊」。
                23. 「目前提供的病歷資料中查無相關資訊」只能在完全沒有任何相關資料時使用。
                24. 部分欄位為空時，應說明該欄位未提供，不得將整筆紀錄判定為查無資料。
                25. TestItem、TestItemCode 與 TestItemName 是不同欄位，不得任意互換。
                26. 回答檢驗項目時，必須優先使用「檢驗項目名稱」；若名稱未提供，才可使用檢驗類型或代碼。
                27. 不得自行將 CBC 改寫成血紅素或血球計數，除非資料中明確提供該名稱。
                28. 使用者詢問特定檢驗名稱時，應同時比對檢驗類型、檢驗代碼及檢驗項目名稱。
                29. 不得僅依異常標記 N 就直接解釋為「正常」，除非資料來源明確定義 N 的意義。
                30. 若同時具有數值結果與參考範圍，可以說明該數值是否位於參考範圍內，但不得將此判斷描述為正式臨床診斷。
                31. 回答異常標記時，應先原樣呈現標記，再說明數值與參考範圍的比較結果。
                32. 使用者使用「如果」、「假設」、「假如」等語句時，應將問題視為假設性比較，不得要求該假設數值必須存在於病歷資料中。
                33. 回答假設性問題時，必須明確標示該數值是假設值，不得描述為病人的實際檢驗結果。
                34. 若參考範圍為 12–16，則端點 12 與 16 均視為位於該範圍內，除非資料另有定義。
                35. 使用者詢問哪些病人的檢驗數值位於參考範圍內時，應逐筆比較 NumericResult 與 ReferenceRange。
                36. 只有 NumericResult 與 ReferenceRange 都可解析為數值時，才可進行範圍比較。
                37. 若數值位於參考範圍上下限之間，包括上下限，應列出病人編號、檢驗日期、檢驗項目、數值及參考範圍。
                38. 無法解析或缺少數值、參考範圍的紀錄應略過，並不得因此將其他有效紀錄判定為查無資料。
                39. 數值位於參考範圍內時，應回答「依目前數值與參考範圍比較，未顯示異常」，不得直接下正式臨床診斷。
                40. 對假設性數值進行範圍比較時，上下限皆包含在參考範圍內。例如參考範圍為 12–16，則 12 與 16 均位於範圍內，不得回答查無相關資訊。
                41. 稽核紀錄只提供每次異動後的資料快照時，不得將所有欄位都描述為已修改；只有比較前後值不同的欄位，才能說明為「由某值改為某值」。
                42. 回答病人身分時，必須使用「病人編號」欄位，例如 P003；不得使用【病人 1】、【病人 2】等 context 排序序號作為病人識別碼。
                43. 使用者要求列出符合特定條件的紀錄時，只能輸出符合條件的紀錄；不得先列出不符合條件的紀錄，再於後文說明不列入。
                44. 「具有數值結果與參考範圍」表示 NumericResult 與 ReferenceRange 均不可為空；任一欄位缺少即不得列入結果。
                45. 若有符合條件的紀錄，直接列出符合者，不需要逐一說明其他病人為何不符合。
                46. 假設性範圍問題若 context 中只有一個可用的參考範圍，可以使用該範圍進行比較，並明確說明這是假設性比較。
                47. 若 context 中存在多個不同的參考範圍，且使用者未指定檢驗項目，不得任意選擇，應請使用者指定檢驗項目。
                48. 不得將「參考範圍」直接改寫為「正常範圍」；應回答數值位於或不位於參考範圍內。
                以下是 HBase 病歷檢索結果：
                {medicalRecordContext}
                """
        }
                };

            foreach (var message in allowedMessages)
            {
                ollamaMessages.Add(new
                {
                    role = message.role,
                    content = message.content
                });
            }

            var ollamaRequest = new
            {
                model = "qwen3:4b-instruct",
                messages = ollamaMessages,
                stream = false,
                think = false,
                keep_alive = "10m",
                options = new
                {
                    temperature = 0.2,
                    num_predict = 1500,
                    num_ctx = 16384
                }
            };

            try
            {
                using var aiTimeout =
                    CancellationTokenSource
                        .CreateLinkedTokenSource(
                            cancellationToken
                        );

                aiTimeout.CancelAfter(
                    TimeSpan.FromSeconds(90)
                );

                using HttpResponseMessage response =
                    await _ollamaClient.PostAsJsonAsync(
                        "/api/chat",
                        ollamaRequest,
                        aiTimeout.Token
                    );

                string responseJson =
                    await response.Content.ReadAsStringAsync(
                        aiTimeout.Token
                    );

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "Ollama API error. StatusCode={StatusCode}, Body={Body}",
                        response.StatusCode,
                        responseJson
                    );

                    return StatusCode(503, new
                    {
                        error = $"AI 模型服務錯誤：{response.StatusCode}"
                    });
                }

                using JsonDocument document =
                    JsonDocument.Parse(responseJson);

                if (!document.RootElement.TryGetProperty(
                        "message",
                        out JsonElement messageElement) ||
                    !messageElement.TryGetProperty(
                        "content",
                        out JsonElement contentElement))
                {
                    _logger.LogError(
                        "Ollama 回傳格式不符預期：{Body}",
                        responseJson
                    );

                    return StatusCode(502, new
                    {
                        error = "AI 模型回傳格式錯誤。"
                    });
                }

                string answer =
     contentElement.GetString()?.Trim() ?? "";

                // 移除常見 Markdown 特殊符號
                answer = answer
                    .Replace("**", "")
                    .Replace("__", "")
                    .Replace("```", "")
                    .Replace("`", "")
                    .Replace("###", "")
                    .Replace("##", "")
                    .Replace("#", "");

                if (string.IsNullOrWhiteSpace(answer))
                {
                    string thinking = "";

                    if (messageElement.TryGetProperty(
                            "thinking",
                            out JsonElement thinkingElement))
                    {
                        thinking = thinkingElement.GetString()?.Trim() ?? "";
                    }

                    _logger.LogWarning(
                        "Ollama content 為空。ThinkingLength={ThinkingLength}, Response={Response}",
                        thinking.Length,
                        responseJson
                    );

                    return StatusCode(502, new
                    {
                        error = string.IsNullOrWhiteSpace(thinking)
                            ? "AI 模型沒有回傳回答內容。"
                            : "AI 模型只有產生思考內容，沒有產生最終答案。"
                    });
                }

                return Ok(new
                {
                    answer,

                    patientReferences =
                    isSuggestedTopic
                       ? new List<object>()
                  : BuildAiPatientReferences(
                    contextualQuestion,
                   allPatients
                    ),

                    images =
                    isSuggestedTopic
                    ? new List<object>()
                    : intent.Intent == "patient_detail"
                    ? BuildAiPatientImages(
                     contextualQuestion,
                     allPatients
            )
            : new List<object>(),
                    videos =
    new List<object>()
                });
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Ollama API timeout.");

                return StatusCode(504, new
                {
                    error = "AI 回應逾時，請稍後再試。"
                });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(
                    ex,
                    "無法連線 Ollama API."
                );

                return StatusCode(503, new
                {
                    error = "無法連線 AI 模型，請確認 Ollama 是否正在執行。"
                });
            }
            catch (JsonException ex)
            {
                _logger.LogError(
                    ex,
                    "無法解析 Ollama response."
                );

                return StatusCode(502, new
                {
                    error = "AI 模型回傳的 JSON 格式錯誤。"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "AskAi 發生未預期錯誤."
                );

                return StatusCode(500, new
                {
                    error = "AI 服務發生未預期錯誤。"
                });
            }
        }

        // =========================
        // 紙本病歷：確認或建立病人
        // =========================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>
            EnsurePaperMedicalRecordPatient(
                [FromBody]
        EnsurePaperPatientRequest request)
        {
            if (!IsLogin())
            {
                return Unauthorized(new
                {
                    success = false,
                    error = "登入狀態已失效，請重新登入。"
                });
            }

            if (!IsTeacher())
            {
                return StatusCode(403, new
                {
                    success = false,
                    error = "只有老師或管理員可以建立紙本病歷病人資料。"
                });
            }

            if (request == null)
            {
                return BadRequest(new
                {
                    success = false,
                    error = "缺少病人資料。"
                });
            }

            string row =
                request.Row?.Trim() ?? "";

            string medicalRecordNumber =
                request.MedicalRecordNumber?.Trim()
                ?? "";

            if (string.IsNullOrWhiteSpace(row))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "缺少病人編號。"
                });
            }

            if (!Regex.IsMatch(
                    row,
                    @"^[A-Za-z0-9_-]{1,30}$"))
            {
                return BadRequest(new
                {
                    success = false,
                    error =
                        "病人編號只能包含英文字母、數字、底線及連字號。"
                });
            }

            if (string.IsNullOrWhiteSpace(
                    medicalRecordNumber))
            {
                return BadRequest(new
                {
                    success = false,
                    error = "缺少病歷號碼。"
                });
            }


            try
            {
                List<Cadaver> existingData =
                    await GetAllData();

                Cadaver? existingPatient =
                    existingData.FirstOrDefault(
                        patient =>
                            string.Equals(
                                patient.RowKey,
                                row,
                                StringComparison
                                    .OrdinalIgnoreCase
                            )
                    );

                /*
                 * 病人編號已存在時，
                 * 驗證病歷號碼與姓名，避免病歷掛錯人。
                 */
                if (existingPatient != null)
                {
                    if (!string.IsNullOrWhiteSpace(
                            existingPatient
                                .MedicalRecordNumber)
                        &&
                        !string.Equals(
                            existingPatient
                                .MedicalRecordNumber,
                            medicalRecordNumber,
                            StringComparison
                                .OrdinalIgnoreCase))
                    {
                        return Conflict(new
                        {
                            success = false,
                            error =
                                $"病人編號 {row} 已存在，" +
                                "但病歷號碼與現有資料不一致。"
                        });
                    }

                    return Ok(new
                    {
                        success = true,
                        created = false,
                        row,
                        message =
                            "病人資料已存在，將直接帶入新增病歷紀錄。"
                    });
                }

                bool duplicateMedicalRecordNumber =
                    existingData.Any(patient =>
                        !string.IsNullOrWhiteSpace(
                            patient.MedicalRecordNumber)
                        &&
                        string.Equals(
                            patient.MedicalRecordNumber,
                            medicalRecordNumber,
                            StringComparison
                                .OrdinalIgnoreCase)
                    );

                if (duplicateMedicalRecordNumber)
                {
                    return Conflict(new
                    {
                        success = false,
                        error =
                            $"病歷號碼 {medicalRecordNumber} 已被其他病人使用。"
                    });
                }

                /*
                 * 新病人尚未具備完整基本資料，
                 * 先建立紙本 AI 匯入用的待補資料。
                 */
                await PutPatientRow(
                    row,
                    medicalRecordNumber,
                    "不詳",
                    "不詳",
                    "病人不知",
                    "待補",
                    "待補",
                    "0",
                    "paper-ai",
                    DateTime.Now.ToString("yyyyMMddHHmmss")
                );

                await SavePatientSnapshot(
                    row,
                    "不詳",
                    "",
                    "",
                    ""
                );

                return Ok(new
                {
                    success = true,
                    created = true,
                    row,
                    message =
                        "病人基本資料已建立，將帶入新增病歷紀錄。"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "建立紙本病歷病人資料失敗。Row={Row}",
                    row
                );

                return StatusCode(
                    StatusCodes
                        .Status500InternalServerError,
                    new
                    {
                        success = false,
                        error = "建立病人資料失敗。",
                        detail =
                            ex.InnerException?.Message
                            ?? ex.Message
                    }
                );
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportMedicalRecord(
    IFormFile medicalRecordFile)
        {
            string currentStep = "開始匯入";

            try
            {
                // 未登入
                if (!IsLogin())
                {
                    return Unauthorized(new
                    {
                        error = "登入狀態已失效，請重新登入。"
                    });
                }

                if (!IsTeacher())
                {
                    return StatusCode(403, new
                    {
                        error = "只有老師或管理員可以使用 AI 病歷查詢。"
                    });
                }
                if (medicalRecordFile == null ||
                    medicalRecordFile.Length == 0)
                {
                    return BadRequest(new
                    {
                        error = "請選擇電子病歷檔案。"
                    });
                }

                const long maxFileSize = 2 * 1024 * 1024;

                if (medicalRecordFile.Length > maxFileSize)
                {
                    return BadRequest(new
                    {
                        error = "檔案不可超過 2 MB。"
                    });
                }

                string extension =
                    Path.GetExtension(
                        medicalRecordFile.FileName
                    ).ToLowerInvariant();

                if (extension != ".json")
                {
                    return BadRequest(new
                    {
                        error = "正式病歷匯入目前只支援 JSON 格式。"
                    });
                }

                string content;

                using (var reader = new StreamReader(
                    medicalRecordFile.OpenReadStream(),
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true))
                {
                    content = await reader.ReadToEndAsync();
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    return BadRequest(new
                    {
                        error = "檔案中沒有可讀取的內容。"
                    });
                }

                MedicalRecordImportDto? importData;

                try
                {
                    importData =
                        JsonSerializer.Deserialize<MedicalRecordImportDto>(
                            content,
                            new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            }
                        );
                }
                catch (JsonException ex)
                {
                    return BadRequest(new
                    {
                        error = "JSON 格式錯誤。",
                        detail = ex.Message
                    });
                }

                if (importData == null)
                {
                    return BadRequest(new
                    {
                        error = "無法解析電子病歷資料。"
                    });
                }

                importData.Row =
                    importData.Row?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(importData.Row))
                {
                    return BadRequest(new
                    {
                        error = "電子病歷缺少 Row 病人編號。"
                    });
                }

                // 檢查 RowKey 是否已存在
                currentStep = "檢查既有資料";
                var existingData = await GetAllData();

                bool rowExists =
    existingData.Any(x =>
        string.Equals(
            x.RowKey,
            importData.Row,
            StringComparison.OrdinalIgnoreCase
        )
    );

                if (rowExists)
                {
                    return Conflict(new
                    {
                        error =
                            $"病人編號 {importData.Row} 已存在，請使用其他病人編號或改用修改功能。"
                    });
                }

                // 寫入 patient:*
                currentStep = "寫入 patient";

                await PutPatientRow(
                    importData.Row,
                    "",
                    importData.Gender,
                    importData.BloodType,
                    "",
                    "",
                    "",
                    "0",
                    string.IsNullOrWhiteSpace(importData.DataSource)
                        ? "import"
                        : importData.DataSource,
                    importData.DataVersion
                );
                // 寫入 teaching:*
                currentStep = "寫入 teaching";
                await PutTeachingRow(
                    importData.Row,
                    importData.DissectionProcessRecord,
                    importData.TeacherExplanationRecord,
                    importData.TeachingNote,
                    importData.PathologicalFindings,
                    importData.RelatedDiagnosisCode,
                    importData.RelatedTestItemCode,
                    importData.RelatedOrderCode
                );

                // 寫入 lab_test:*
                currentStep = "寫入 lab_test";

                string importedLabRecordId =
                    Guid.NewGuid().ToString("N");

                await PutLabTestRow(
                   importData.Row,
                   importedLabRecordId,
                   importData.LabTestDate,
                   importData.LabTestItem,
                   importData.LabTestResult,
                   importData.LabTestItemCode,
                   importData.LabTestItemName,
                   importData.LabResultType,
                   importData.LabNumericResult,
                   importData.LabTextResult,
                   importData.LabImageResultPath,
                   importData.LabWaveformResultPath,
                   importData.LabAudioResultPath,

                   "",
                   "",
                   "",
                   "",
                   "",
                   "",

                   importData.LabUnit,
                   importData.LabReferenceRange,
                   importData.LabAbnormalFlag,
                   importData.LabTestMethod,
                   importData.LabSpecimenCollectedAt
               );
                // 寫入 admission:*
                currentStep = "寫入 admission";
                await PutAdmissionRow(
                    importData.Row,
                    importData.AdmissionStartDate,
                    importData.AdmissionEndDate,
                    importData.WardType,
                    importData.AdmissionReason,
                    importData.AdmissionReasonCode,
                    importData.AdmissionDiagnosisCode,
                    importData.DischargeDiagnosisCode,
                    importData.PrimaryDiagnosisCode,
                    importData.SecondaryDiagnosisCode,
                    importData.HospitalCode,
                    importData.DepartmentCode,
                    importData.DischargeStatus,
                    importData.LengthOfStay,
                    importData.IcuDays,
                    importData.InpatientOrderCode,
                    importData.ProcedureCode
                );

                // 建立病人 FHIR-like 快照
                currentStep = "建立病人 FHIR-like 快照";
                await SavePatientSnapshot(
                    importData.Row,
                    importData.Gender,
                    importData.Age,
                    importData.DonationDate,
                    importData.DissectionDate
                );

                // 建立匯入歷程
                currentStep = "建立電子病歷匯入歷程";
                string recordId =
                    Guid.NewGuid().ToString("N");

                string finding =
                    $"由電子病歷檔案匯入資料；病人編號={importData.Row}，" +
                    $"性別={importData.Gender}，年齡={importData.Age}，" +
                    $"主要診斷={importData.PrimaryDiagnosisCode}。";

                await PutMedicalRecord(
                    importData.Row,
                    new MedicalRecord
                    {
                        Id = recordId,
                        ResourceType = "Observation",
                        OccurredAt =
                            DateTime.Now.ToString(
                                "yyyy-MM-ddTHH:mm:ss"
                            ),
                        Diagnosis = "ElectronicMedicalRecordImported",
                        BodyPart = "電子病歷匯入",
                        Finding = finding,
                        Treatment = "",
                        Physician =
                            HttpContext.Session.GetString("LoginId")
                            ?? "系統",
                        Note =
                            $"由檔案 {Path.GetFileName(medicalRecordFile.FileName)} 匯入。",
                        FhirJson = BuildObservationFhirJson(
                            importData.Row,
                            recordId,
                            DateTime.Now.ToString(
                                "yyyy-MM-ddTHH:mm:ss"
                            ),
                            "ElectronicMedicalRecordImported",
                            "電子病歷匯入",
                            finding,
                            "",
                            HttpContext.Session.GetString("LoginId")
                                ?? "系統",
                            "電子病歷檔案匯入"
                        )
                    }
                );

                return Ok(new
                {
                    success = true,
                    row = importData.Row,
                    message =
                        $"電子病歷 {importData.Row} 已新增至資料列表。"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "匯入電子病歷時發生錯誤，錯誤階段：{CurrentStep}",
                    currentStep
                );

                string errorMessage =
                    ex.InnerException?.Message
                    ?? ex.Message;

                return StatusCode(500, new
                {
                    error = "電子病歷匯入失敗。",
                    detail =
                        $"錯誤階段：{currentStep}；原因：{errorMessage}"
                });
            }
        }
        // =========================
        // 刪除整筆資料
        // =========================
        [HttpPost]
        public async Task<IActionResult> Delete(string row)
        {
            var block = RequireTeacher();
            if (block != null) return block;

            if (string.IsNullOrWhiteSpace(row))
            {
                TempData["Message"] = "刪除失敗：缺少 RowKey";
                return RedirectToAction("Index");
            }

            await _client.DeleteAsync($"cadaver/{row}");
            TempData["Message"] = "資料已刪除";
            return RedirectToAction("Index");
        }

        // =========================
        // 搜尋
        // 1. 病人編號
        // 2. 近幾年內
        // 3. 生前影像分類
        // =========================
        [HttpGet]
        public async Task<IActionResult> Search(
    string disease,
    string bodyPart,
    int? years,
    string videoKeyword,
    string videoCategory)
        {
            var block = RequireLogin();
            if (block != null) return block;

            var data = await GetAllData();

            // 疾病：建議查影片分類、影片說明
            if (!string.IsNullOrWhiteSpace(disease))
            {
                data = data.Where(x =>
                    (x.VideoCategories != null &&
                     x.VideoCategories.Any(c =>
                         !string.IsNullOrWhiteSpace(c) &&
                         c.Contains(disease, StringComparison.OrdinalIgnoreCase))) ||

                    (x.VideoNotes != null &&
                     x.VideoNotes.Any(n =>
                         !string.IsNullOrWhiteSpace(n) &&
                         n.Contains(disease, StringComparison.OrdinalIgnoreCase)))
                ).ToList();
            }

            // 部位：建議查影片分類、影片說明
            if (!string.IsNullOrWhiteSpace(bodyPart))
            {
                data = data.Where(x =>
                    (x.VideoCategories != null &&
                     x.VideoCategories.Any(c =>
                         !string.IsNullOrWhiteSpace(c) &&
                         c.Contains(bodyPart, StringComparison.OrdinalIgnoreCase))) ||

                    (x.VideoNotes != null &&
                     x.VideoNotes.Any(n =>
                         !string.IsNullOrWhiteSpace(n) &&
                         n.Contains(bodyPart, StringComparison.OrdinalIgnoreCase)))
                ).ToList();
            }

            // 幾年內：依解剖日期
            if (years.HasValue)
            {
                var cutoff = DateTime.Now.AddYears(-years.Value);

                data = data.Where(x =>
                {
                    if (DateTime.TryParse(x.DissectionDate, out var d))
                        return d >= cutoff;

                    return false;
                }).ToList();
            }

            // 解剖影片分類
            if (!string.IsNullOrWhiteSpace(videoCategory))
            {
                data = data.Where(x =>
                    x.VideoCategories != null &&
                    x.VideoCategories.Any(c =>
                        !string.IsNullOrWhiteSpace(c) &&
                        c.Contains(videoCategory, StringComparison.OrdinalIgnoreCase)
                    )
                ).ToList();
            }

            // 影片關鍵字：找影片分類 / 影片說明 / 影片路徑
            if (!string.IsNullOrWhiteSpace(videoKeyword))
            {
                data = data.Where(x =>
                    (
                        x.VideoCategories != null &&
                        x.VideoCategories.Any(c =>
                            !string.IsNullOrWhiteSpace(c) &&
                            c.Contains(videoKeyword, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    ||
                    (
                        x.VideoNotes != null &&
                        x.VideoNotes.Any(n =>
                            !string.IsNullOrWhiteSpace(n) &&
                            n.Contains(videoKeyword, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    ||
                    (
                        x.VideoPaths != null &&
                        x.VideoPaths.Any(p =>
                            !string.IsNullOrWhiteSpace(p) &&
                            p.Contains(videoKeyword, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                ).ToList();
            }

            return View("Index", data);
        }
        [HttpPost]
        public async Task<IActionResult> DeleteImage(
    [FromBody] DeleteImageRequest req)
        {
            var block =
                RequireTeacher();

            if (block != null)
                return block;


            // =========================
            // 基本驗證
            // =========================

            if (req == null)
            {
                return BadRequest(
                    "缺少刪除圖片資料。"
                );
            }

            string rowKey =
                req.RowKey?.Trim() ?? "";

            string imgPath =
                req.ImgPath?.Trim() ?? "";

            string imageType =
                req.ImageType?.Trim()
                    .ToLowerInvariant()
                ?? "";


            if (string.IsNullOrWhiteSpace(
                    rowKey))
            {
                return BadRequest(
                    "缺少 RowKey。"
                );
            }

            if (string.IsNullOrWhiteSpace(
                    imgPath))
            {
                return BadRequest(
                    "缺少圖片路徑。"
                );
            }

            if (imageType != "course" &&
            imageType != "medical" &&
            imageType != "pathology" &&
            imageType != "legacy")
            {
                return BadRequest(
                    "影像類型錯誤。"
                );
            }


            // =========================
            // 取得目前病人資料
            // =========================

            var data =
                await GetAllData();

            var cadaver =
                data.FirstOrDefault(x =>
                    string.Equals(
                        x.RowKey,
                        rowKey,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (cadaver == null)
            {
                return BadRequest(
                    $"找不到病人資料：{rowKey}"
                );
            }


            // =========================
            // 根據影像類型找 Image ID
            // =========================

            string imageId =
                "";

            string family =
                "";

            if (imageType == "course")
            {
                int index =
                    cadaver.CourseImagePaths
                        .FindIndex(path =>
                            string.Equals(
                                path?.Trim(),
                                imgPath,
                                StringComparison.OrdinalIgnoreCase
                            )
                        );

                if (index < 0)
                {
                    return BadRequest(
                        "找不到此課程影像。"
                    );
                }

                if (index >=
                    cadaver.CourseImageIds.Count)
                {
                    return BadRequest(
                        "課程影像缺少 Image ID。"
                    );
                }

                imageId =
                    cadaver.CourseImageIds[index];

                family =
                    "course_image";
            }
            else if (
            imageType == "medical" ||
            imageType == "pathology")
            {
                int index =
                    cadaver.MedicalImagePaths
                        .FindIndex(path =>
                            string.Equals(
                                path?.Trim(),
                                imgPath,
                                StringComparison.OrdinalIgnoreCase
                            )
                        );

                if (index < 0)
                {
                    return BadRequest(
                        "找不到此病歷影像。"
                    );
                }

                if (index >=
                    cadaver.MedicalImageIds.Count)
                {
                    return BadRequest(
                        "病歷影像缺少 Image ID。"
                    );
                }

                imageId =
                    cadaver.MedicalImageIds[index];

                family =
                    "medical_image";
            }
            else
            {
                int index =
                    cadaver.ImagePaths
                        .FindIndex(path =>
                            string.Equals(
                                path?.Trim(),
                                imgPath,
                                StringComparison.OrdinalIgnoreCase
                            )
                        );

                if (index < 0)
                {
                    return BadRequest(
                        "找不到此舊版影像。"
                    );
                }

                if (index >=
                    cadaver.ImageIds.Count)
                {
                    return BadRequest(
                        "舊版影像缺少 Image ID。"
                    );
                }

                imageId =
                    cadaver.ImageIds[index];

                family =
                    "info";
            }


            if (string.IsNullOrWhiteSpace(
                    imageId))
            {
                return BadRequest(
                    "找不到 Image ID。"
                );
            }


            // =========================
            // 要刪除的 HBase 欄位
            // =========================

            var columns =
                new List<string>();


            if (imageType == "legacy")
            {
                columns.Add(
                    $"info:image_{imageId}"
                );

                columns.Add(
                    $"info:note_{imageId}"
                );

                columns.Add(
                    $"info:isAlive_{imageId}"
                );

                columns.Add(
                    $"info:drawing_{imageId}"
                );

                columns.Add(
                    $"info:anno_img_{imageId}"
                );
            }
            else
            {
                columns.Add(
                    $"{family}:image_{imageId}"
                );

                columns.Add(
                    $"{family}:note_{imageId}"
                );

                columns.Add(
                    $"{family}:drawing_{imageId}"
                );

                columns.Add(
                    $"{family}:anno_img_{imageId}"
                );
            }


            // =========================
            // 只刪指定欄位
            // 絕對不刪整個 Row
            // =========================

            foreach (string column in columns)
            {
                string encodedRow =
                    Uri.EscapeDataString(
                        rowKey
                    );

                string encodedColumn =
                    Uri.EscapeDataString(
                        column
                    );

                var response =
                    await _client.DeleteAsync(
                        $"cadaver/{encodedRow}/{encodedColumn}"
                    );


                /*
                 * 某些欄位可能本來就不存在，
                 * 例如沒有 drawing 或 anno_img。
                 *
                 * 404 可以視為已不存在，
                 * 其他錯誤才停止。
                 */
                if (!response.IsSuccessStatusCode &&
                    response.StatusCode !=
                    System.Net.HttpStatusCode.NotFound)
                {
                    string error =
                        await response.Content
                            .ReadAsStringAsync();

                    _logger.LogError(
                        "刪除 HBase 圖片欄位失敗。" +
                        " RowKey={RowKey}," +
                        " Column={Column}," +
                        " Status={Status}," +
                        " Body={Body}",
                        rowKey,
                        column,
                        response.StatusCode,
                        error
                    );

                    return StatusCode(
                        500,
                        $"刪除圖片資料失敗：{column}"
                    );
                }
            }


            // =========================
            // 刪除實體圖片
            // =========================

            try
            {
                string relativePath =
                    imgPath
                        .TrimStart('/')
                        .Replace(
                            '/',
                            Path.DirectorySeparatorChar
                        );

                string fullPath =
                    Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "wwwroot",
                        relativePath
                    );

                string wwwrootPath =
                    Path.GetFullPath(
                        Path.Combine(
                            Directory.GetCurrentDirectory(),
                            "wwwroot"
                        )
                    );

                string normalizedFullPath =
                    Path.GetFullPath(
                        fullPath
                    );


                /*
                 * 防止 ../ 之類的路徑跳脫
                 */
                if (normalizedFullPath.StartsWith(
                        wwwrootPath,
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    System.IO.File.Exists(
                        normalizedFullPath))
                {
                    System.IO.File.Delete(
                        normalizedFullPath
                    );
                }
            }
            catch (Exception ex)
            {
                /*
                 * HBase 已經刪除成功時，
                 * 實體檔案刪除失敗不應把病歷 Row 搞壞。
                 */
                _logger.LogWarning(
                    ex,
                    "圖片 HBase 資料已刪除，" +
                    "但實體圖片刪除失敗。" +
                    " RowKey={RowKey}," +
                    " ImgPath={ImgPath}",
                    rowKey,
                    imgPath
                );
            }


            return Ok(
                new
                {
                    success = true,
                    rowKey,
                    imageId,
                    imageType
                }
            );
        }
        // 上傳圖片 / 影片
        [HttpPost]
        public async Task<IActionResult> UploadImage(
    string row,
    string imageType,
    string examinationType,
    string symptomCategory,
    List<IFormFile> files,
    List<IFormFile> videos,
    List<string> notes,
    List<string> isAliveFlags)
        {
            var block = RequireTeacher();
            if (block != null) return block;
            row =
            row?.Trim() ?? "";

            imageType =
                imageType?.Trim()
                    .ToLowerInvariant()
                ?? "";

            examinationType =
            examinationType?.Trim()
             ?? "";

            symptomCategory =
                symptomCategory?.Trim()
                ?? "";
            if (imageType == "pathology")
            {
                examinationType = "Pathology";
            }

            if (string.IsNullOrWhiteSpace(row))
            {
                TempData["Message"] =
                    "上傳失敗：缺少病人編號。";

                return RedirectToAction("Index");
            }

            if (imageType != "course" &&
            imageType != "medical" &&
            imageType != "pathology")
            {
                TempData["Message"] =
                    "上傳失敗：請選擇影像類型。";

                return RedirectToAction("Index");
            }

            string imageFolderName =
        imageType == "course"
        ? "course-images"
        : imageType == "pathology"
            ? "pathology-images"
            : "medical-images";

            var imageFolder =
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    imageFolderName
                );

            var videoFolder =
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "videos"
                );

            string normalizedHBaseExamType =
                NormalizeHBaseExaminationType(
                    examinationType
                );

            bool saveImageDirectlyToHBase =
                imageType == "medical"
                &&
                (
                    normalizedHBaseExamType == "xray"
                    ||
                    normalizedHBaseExamType == "ultrasound"
                )
                &&
                IsLungMediaCategory(
                    symptomCategory
                );

            // 肺部 X 光 / 肺部超音波不需要建立本地圖片資料夾。
            // 其他既有影像功能仍維持原本 wwwroot 儲存方式。
            if (!saveImageDirectlyToHBase)
            {
                Directory.CreateDirectory(
                    imageFolder
                );
            }

            Directory.CreateDirectory(
                videoFolder
            );

            int noteIndex = 0;

            if (files != null)
            {
                for (
                    int i = 0;
                    i < files.Count;
                    i++)
                {
                    var file =
                        files[i];

                    if (file == null
                        || file.Length == 0)
                    {
                        continue;
                    }

                    string note =
                        noteIndex
                            < (notes?.Count ?? 0)
                            ? notes[noteIndex]
                            : "";

                    if (saveImageDirectlyToHBase)
                    {
                        // 直接將圖片 bytes 存入 medical_media，
                        // 不寫入 App_Data / wwwroot。
                        await SaveHBaseLungMedicalImage(
                            row,
                            examinationType,
                            symptomCategory,
                            file,
                            note
                        );

                        noteIndex++;
                        continue;
                    }

                    var ext =
                        Path.GetExtension(
                            file.FileName
                        );

                    if (string.IsNullOrWhiteSpace(
                            ext))
                    {
                        ext = ".jpg";
                    }

                    var fileName =
                        $"{row}_{Guid.NewGuid():N}{ext}";

                    var fullPath =
                        Path.Combine(
                            imageFolder,
                            fileName
                        );

                    using (
                        var stream =
                            new FileStream(
                                fullPath,
                                FileMode.Create
                            ))
                    {
                        await file.CopyToAsync(
                            stream
                        );
                    }

                    await SaveCategorizedImage(
                        row,
                        imageType,
                        $"/{imageFolderName}/{fileName}",
                        note,
                        examinationType,
                        symptomCategory
                    );

                    noteIndex++;
                }
            }

            int videoIndex = 0;

            if (videos != null)
            {
                foreach (var video in videos)
                {
                    if (video == null || video.Length == 0) continue;

                    var ext = Path.GetExtension(video.FileName);
                    if (string.IsNullOrWhiteSpace(ext)) ext = ".mp4";

                    var fileName = $"{row}_{Guid.NewGuid():N}{ext}";
                    var fullPath = Path.Combine(videoFolder, fileName);

                    using (var stream = new FileStream(fullPath, FileMode.Create))
                    {
                        await video.CopyToAsync(stream);
                    }

                    string note = noteIndex < (notes?.Count ?? 0) ? notes[noteIndex] : "";



                    bool isAlive = false;

                    if (noteIndex < (isAliveFlags?.Count ?? 0))
                    {
                        isAlive = isAliveFlags[noteIndex] == "true";
                    }

                    await SaveVideo(
                    row,
                    $"/videos/{fileName}",
                    note,
                    examinationType,
                    symptomCategory,
                    isAlive,
                    imageType
                    );

                    noteIndex++;
                    videoIndex++;
                }
            }

            TempData["Message"] = "媒體已上傳";

            return RedirectToAction("Index");
        }

        // =========================
        // 病歷獨立管理與查詢
        // =========================
        [HttpGet]
        public async Task<IActionResult> MedicalRecordManagement(
            string startDate = "",
            string endDate = "",
            string disease = "",
            string row = "",
            string medicalRecordNumber = "")
        {
            var block = RequireLogin();

            if (block != null)
            {
                return block;
            }

            startDate = startDate?.Trim() ?? "";
            endDate = endDate?.Trim() ?? "";
            disease = disease?.Trim() ?? "";
            row = row?.Trim() ?? "";
            medicalRecordNumber =
                medicalRecordNumber?.Trim() ?? "";

            var patients = await GetAllData();

            var results = new List<MedicalRecordSearchItem>();

            foreach (var patient in patients)
            {
                // 病人編號篩選
                if (!string.IsNullOrWhiteSpace(row) &&
                    !patient.RowKey.Contains(
                        row,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 病歷號碼篩選
                if (!string.IsNullOrWhiteSpace(medicalRecordNumber) &&
                    !(patient.MedicalRecordNumber ?? "").Contains(
                        medicalRecordNumber,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var record in patient.MedicalRecords)
                {
                    DateTime? recordDate = null;

                    if (DateTime.TryParse(
                        record.OccurredAt,
                        out DateTime parsedDate))
                    {
                        recordDate = parsedDate;
                    }

                    // 開始日期篩選
                    if (!string.IsNullOrWhiteSpace(startDate) &&
                        DateTime.TryParse(
                            startDate,
                            out DateTime parsedStartDate))
                    {
                        if (!recordDate.HasValue ||
                            recordDate.Value.Date <
                            parsedStartDate.Date)
                        {
                            continue;
                        }
                    }

                    // 結束日期篩選
                    if (!string.IsNullOrWhiteSpace(endDate) &&
                        DateTime.TryParse(
                            endDate,
                            out DateTime parsedEndDate))
                    {
                        if (!recordDate.HasValue ||
                            recordDate.Value.Date >
                            parsedEndDate.Date)
                        {
                            continue;
                        }
                    }

                    // 病症關鍵字篩選
                    if (!string.IsNullOrWhiteSpace(disease))
                    {
                        bool matched =
                            (record.Diagnosis ?? "").Contains(
                                disease,
                                StringComparison.OrdinalIgnoreCase)
                            ||
                            (record.BodyPart ?? "").Contains(
                                disease,
                                StringComparison.OrdinalIgnoreCase)
                            ||
                            (record.Finding ?? "").Contains(
                                disease,
                                StringComparison.OrdinalIgnoreCase)
                            ||
                            (record.Treatment ?? "").Contains(
                                disease,
                                StringComparison.OrdinalIgnoreCase)
                            ||
                            (record.Note ?? "").Contains(
                                disease,
                                StringComparison.OrdinalIgnoreCase);

                        if (!matched)
                        {
                            continue;
                        }
                    }

                    results.Add(new MedicalRecordSearchItem
                    {
                        RowKey = patient.RowKey,
                        PatientId = patient.PatientId,
                        MedicalRecordNumber =
                            patient.MedicalRecordNumber,

                        Gender = patient.Gender,
                        IsDissected = patient.IsDissected,

                        RecordId = record.Id,
                        OccurredAt = record.OccurredAt,
                        Diagnosis = record.Diagnosis,
                        BodyPart = record.BodyPart,
                        Finding = record.Finding,
                        Treatment = record.Treatment,
                        Physician = record.Physician,
                        Note = record.Note,
                        ResourceType = record.ResourceType
                    });
                }
            }

            results = results
                .OrderByDescending(x =>
                    DateTime.TryParse(
                        x.OccurredAt,
                        out DateTime d)
                        ? d
                        : DateTime.MinValue)
                .ToList();

            ViewBag.StartDate = startDate;
            ViewBag.EndDate = endDate;
            ViewBag.Disease = disease;
            ViewBag.Row = row;
            ViewBag.MedicalRecordNumber =
                medicalRecordNumber;

            return View(results);
        }
        // =========================
        // 病歷調閱頁面
        // =========================
        [HttpGet]
        public async Task<IActionResult> MedicalRecords(
    string row = "",
    string patientNumber = "",
    string keyword = "",
    string medicalRecordNumber = "",
    string startDate = "",
    string endDate = "")
        {
            var block =
                RequireLogin();

            if (block != null)
            {
                return block;
            }

            row =
                row?.Trim() ?? "";

            patientNumber =
                patientNumber?.Trim() ?? "";

            keyword =
                keyword?.Trim() ?? "";

            medicalRecordNumber =
                medicalRecordNumber?.Trim() ?? "";

            startDate =
                startDate?.Trim() ?? "";

            endDate =
                endDate?.Trim() ?? "";


            bool hasSearchCondition =
                !string.IsNullOrWhiteSpace(
                    patientNumber
                )
                ||
                !string.IsNullOrWhiteSpace(keyword)
                ||
                !string.IsNullOrWhiteSpace(
                    medicalRecordNumber
                )
                ||
                !string.IsNullOrWhiteSpace(startDate)
                ||
                !string.IsNullOrWhiteSpace(endDate);


            var data =
                await GetAllData();
            Cadaver? currentPatient =
    null;

            if (!string.IsNullOrWhiteSpace(row))
            {
                currentPatient =
                    data.FirstOrDefault(x =>
                        string.Equals(
                            x.RowKey,
                            row,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );
            }

            ViewBag.CurrentPatient =
                currentPatient;

            /*
             * 若完全沒有搜尋條件，
             * 不顯示任何紀錄。
             */
            if (!hasSearchCondition)
            {
                ViewBag.SearchPerformed =
                    false;

                ViewBag.Keyword =
                    keyword;

                ViewBag.Row =
                    row;

                ViewBag.MedicalRecordNumber =
                    medicalRecordNumber;

                ViewBag.StartDate =
                    startDate;

                ViewBag.EndDate =
                    endDate;

                return View(
                    new List<
                        MedicalRecordPatientSearchResult
                    >()
                );
            }


            DateTime? parsedStartDate =
                null;

            DateTime? parsedEndDate =
                null;


            if (
                DateTime.TryParse(
                    startDate,
                    out DateTime start
                )
            )
            {
                parsedStartDate =
                    start.Date;
            }


            if (
                DateTime.TryParse(
                    endDate,
                    out DateTime end
                )
            )
            {
                parsedEndDate =
                    end.Date;
            }


            var searchResults =
                new List<
                    MedicalRecordPatientSearchResult
                >();


            foreach (var patient in data)
            {
                /*
                 * =========================
                 * 病人編號
                 * =========================
                 */

                if (
                    !string.IsNullOrWhiteSpace(
                        patientNumber
                    )
                    &&
                    !(patient.RowKey ?? "")
                        .Contains(
                            patientNumber,
                            StringComparison.OrdinalIgnoreCase
                        )
                )
                {
                    continue;
                }


                /*
                 * =========================
                 * 病歷號碼
                 * =========================
                 */

                if (
                    !string.IsNullOrWhiteSpace(
                        medicalRecordNumber
                    )
                    &&
                    !(patient.MedicalRecordNumber ?? "")
                        .Contains(
                            medicalRecordNumber,
                            StringComparison
                                .OrdinalIgnoreCase
                        )
                )
                {
                    continue;
                }


                var matchedMedicalRecords =
                    new List<MedicalRecord>();

                var matchedLabRecords =
                    new List<LabTestRecord>();

                bool admissionMatched =
                    false;


                /*
                 * =========================
                 * 過往病歷紀錄
                 * =========================
                 */

                foreach (
                    var record
                    in patient.MedicalRecords
                        ?? new List<MedicalRecord>()
                )
                {
                    bool matched =
                        true;


                    // 關鍵字
                    if (
                        !string.IsNullOrWhiteSpace(
                            keyword
                        )
                    )
                    {
                        matched =
                            (record.Diagnosis ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (record.BodyPart ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (record.Finding ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (record.Treatment ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (record.Physician ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (record.Note ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                );
                    }


                    if (!matched)
                    {
                        continue;
                    }


                    // 日期
                    if (
                        parsedStartDate.HasValue
                        ||
                        parsedEndDate.HasValue
                    )
                    {
                        if (
                            !DateTime.TryParse(
                                record.OccurredAt,
                                out DateTime recordDate
                            )
                        )
                        {
                            continue;
                        }


                        if (
                            parsedStartDate.HasValue
                            &&
                            recordDate.Date
                            <
                            parsedStartDate.Value
                        )
                        {
                            continue;
                        }


                        if (
                            parsedEndDate.HasValue
                            &&
                            recordDate.Date
                            >
                            parsedEndDate.Value
                        )
                        {
                            continue;
                        }
                    }


                    matchedMedicalRecords.Add(
                        record
                    );
                }


                /*
                 * =========================
                 * 歷次檢驗紀錄
                 * =========================
                 */

                foreach (
                    var lab
                    in patient.LabTestRecords
                        ?? new List<LabTestRecord>()
                )
                {
                    bool matched =
                        true;


                    if (
                        !string.IsNullOrWhiteSpace(
                            keyword
                        )
                    )
                    {
                        matched =
                            (lab.TestItem ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (lab.TestItemName ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (lab.TestResult ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (lab.TextResult ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (lab.ExaminationType ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (lab.BodyPart ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (lab.Interpretation ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (lab.AbnormalFlag ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (lab.TestMethod ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                );
                    }


                    if (!matched)
                    {
                        continue;
                    }


                    if (
                        parsedStartDate.HasValue
                        ||
                        parsedEndDate.HasValue
                    )
                    {
                        if (
                            !DateTime.TryParse(
                                lab.TestDate,
                                out DateTime labDate
                            )
                        )
                        {
                            continue;
                        }


                        if (
                            parsedStartDate.HasValue
                            &&
                            labDate.Date
                            <
                            parsedStartDate.Value
                        )
                        {
                            continue;
                        }


                        if (
                            parsedEndDate.HasValue
                            &&
                            labDate.Date
                            >
                            parsedEndDate.Value
                        )
                        {
                            continue;
                        }
                    }


                    matchedLabRecords.Add(
                        lab
                    );
                }


                /*
                 * =========================
                 * 住院資料
                 * =========================
                 */

                bool hasAdmissionData =
                    !string.IsNullOrWhiteSpace(
                        patient.AdmissionStartDate
                    )
                    ||
                    !string.IsNullOrWhiteSpace(
                        patient.AdmissionEndDate
                    )
                    ||
                    !string.IsNullOrWhiteSpace(
                        patient.WardType
                    )
                    ||
                    !string.IsNullOrWhiteSpace(
                        patient.AdmissionReason
                    );


                if (hasAdmissionData)
                {
                    bool keywordMatched =
                        true;


                    if (
                        !string.IsNullOrWhiteSpace(
                            keyword
                        )
                    )
                    {
                        keywordMatched =
                            (patient.AdmissionReason ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (patient.AdmissionReasonCode ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (patient.AdmissionDiagnosisCode ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (patient.PrimaryDiagnosisCode ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (patient.SecondaryDiagnosisCode ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (patient.DischargeDiagnosisCode ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (patient.WardType ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (patient.HospitalCode ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (patient.DepartmentCode ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (patient.DischargeStatus ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (patient.InpatientOrderCode ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                )
                            ||
                            (patient.ProcedureCode ?? "")
                                .Contains(
                                    keyword,
                                    StringComparison
                                        .OrdinalIgnoreCase
                                );
                    }


                    bool dateMatched =
                        true;


                    if (
                        parsedStartDate.HasValue
                        ||
                        parsedEndDate.HasValue
                    )
                    {
                        if (
                            DateTime.TryParse(
                                patient.AdmissionStartDate,
                                out DateTime admissionDate
                            )
                        )
                        {
                            if (
                                parsedStartDate.HasValue
                                &&
                                admissionDate.Date
                                <
                                parsedStartDate.Value
                            )
                            {
                                dateMatched =
                                    false;
                            }


                            if (
                                parsedEndDate.HasValue
                                &&
                                admissionDate.Date
                                >
                                parsedEndDate.Value
                            )
                            {
                                dateMatched =
                                    false;
                            }
                        }
                        else
                        {
                            dateMatched =
                                false;
                        }
                    }


                    admissionMatched =
                        keywordMatched
                        &&
                        dateMatched;
                }


                /*
                 * =========================
                 * 是否加入搜尋結果
                 * =========================
                 */

                bool matchedAnyRecord =
                    matchedMedicalRecords.Count > 0
                    ||
                    matchedLabRecords.Count > 0
                    ||
                    admissionMatched;


                /*
                 * 只有輸入病人編號或病歷號碼，
                 * 沒有 keyword / 日期時，
                 * 要顯示該病人的所有分類。
                 */
                bool patientOnlySearch =
                    string.IsNullOrWhiteSpace(keyword)
                    &&
                    !parsedStartDate.HasValue
                    &&
                    !parsedEndDate.HasValue;

                if (
                    patientOnlySearch
                    &&
                    (
                        !string.IsNullOrWhiteSpace(
                            patientNumber
                        )
                        ||
                        !string.IsNullOrWhiteSpace(
                            medicalRecordNumber
                        )
                    )
                )
                {
                    matchedMedicalRecords =
                        patient.MedicalRecords
                        ?? new List<MedicalRecord>();

                    matchedLabRecords =
                        patient.LabTestRecords
                        ?? new List<LabTestRecord>();

                    admissionMatched =
                        hasAdmissionData;

                    matchedAnyRecord =
                        true;
                }


                if (!matchedAnyRecord)
                {
                    continue;
                }


                searchResults.Add(
                    new MedicalRecordPatientSearchResult
                    {
                        Patient =
                            patient,

                        MedicalRecords =
                            matchedMedicalRecords
                                .OrderByDescending(x =>
                                    DateTime.TryParse(
                                        x.OccurredAt,
                                        out DateTime d
                                    )
                                        ? d
                                        : DateTime.MinValue
                                )
                                .ToList(),

                        LabTestRecords =
                            matchedLabRecords
                                .OrderByDescending(x =>
                                    DateTime.TryParse(
                                        x.TestDate,
                                        out DateTime d
                                    )
                                        ? d
                                        : DateTime.MinValue
                                )
                                .ToList(),

                        AdmissionMatched =
                            admissionMatched
                    }
                );
            }


            ViewBag.SearchPerformed =
                true;

            ViewBag.Keyword =
                keyword;

            ViewBag.Row =
                row;
            ViewBag.PatientNumber =
    patientNumber;
            ViewBag.MedicalRecordNumber =
                medicalRecordNumber;

            ViewBag.StartDate =
                startDate;

            ViewBag.EndDate =
                endDate;


            return View(
                searchResults
            );
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveTeachingRecord(
    string row,
    string donationDate,
    string dissectionDate,
    string dissectionProcessRecord,
    string teacherExplanationRecord,
    string teachingNote,
    string pathologicalFindings,
    string relatedDiagnosisCode,
    string relatedTestItemCode,
    string relatedOrderCode)
        {
            var block = RequireTeacher();

            if (block != null)
            {
                return block;
            }

            row = row?.Trim() ?? "";
            donationDate = donationDate?.Trim() ?? "";
            dissectionDate = dissectionDate?.Trim() ?? "";
            dissectionProcessRecord =
                dissectionProcessRecord?.Trim() ?? "";
            teacherExplanationRecord =
                teacherExplanationRecord?.Trim() ?? "";
            teachingNote = teachingNote?.Trim() ?? "";
            pathologicalFindings =
                pathologicalFindings?.Trim() ?? "";
            relatedDiagnosisCode =
    relatedDiagnosisCode?.Trim() ?? "";

            relatedTestItemCode =
                relatedTestItemCode?.Trim() ?? "";

            relatedOrderCode =
                relatedOrderCode?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(row))
            {
                TempData["Message"] = "缺少病人編號";

                return RedirectToAction("Index");
            }

            var data = await GetAllData();

            var cadaver = data.FirstOrDefault(x =>
                string.Equals(
                    x.RowKey,
                    row,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            if (cadaver == null)
            {
                TempData["Message"] =
                    $"找不到病人資料：{row}";

                return RedirectToAction("Index");
            }

            try
            {
                // 儲存捐贈日期與解剖日期
                await PutDissectionDates(
                    row,
                    donationDate,
                    dissectionDate
                );

                // 儲存教學與解剖紀錄
                await PutTeachingRow(
                    row,
                    dissectionProcessRecord,
                    teacherExplanationRecord,
                    teachingNote,
                    pathologicalFindings,
                    relatedDiagnosisCode,
                    relatedTestItemCode,
                    relatedOrderCode
                );

                TempData["Message"] =
                    "捐贈、解剖與教學資料已儲存";
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "儲存解剖與教學資料失敗，RowKey={RowKey}",
                    row
                );

                TempData["Message"] =
                    "儲存失敗，請確認 HBase REST API 是否正常運作";
            }

            return RedirectToAction(
                "MedicalRecords",
                new { row }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveLabTestRecord(
    string row,
    string labTestDate,
    string labTestItem,
    string labTestResult,
    string labTestItemCode,
    string labTestItemName,
    string labResultType,
    string labNumericResult,
    string labTextResult,
    string labImageResultPath,
    string labWaveformResultPath,
    string labAudioResultPath,

    string labExaminationType,
    string labBodyPart,
    string labInterpretation,
    string labDicomResultPath,
    string labVideoResultPath,
    string labReportFilePath,

    string labUnit,
    string labReferenceRange,
    string labAbnormalFlag,
    string labTestMethod,
    string labSpecimenCollectedAt)
        {
            var block = RequireTeacher();

            if (block != null)
            {
                return block;
            }

            row = row?.Trim() ?? "";

            labTestDate =
                labTestDate?.Trim() ?? "";

            labTestItem =
                labTestItem?.Trim() ?? "";

            labTestResult =
                labTestResult?.Trim() ?? "";

            labTestItemCode =
                labTestItemCode?.Trim() ?? "";

            labTestItemName =
                labTestItemName?.Trim() ?? "";

            labResultType =
                labResultType?.Trim() ?? "";

            labNumericResult =
                labNumericResult?.Trim() ?? "";

            labTextResult =
                labTextResult?.Trim() ?? "";

            labImageResultPath =
                labImageResultPath?.Trim() ?? "";

            labWaveformResultPath =
                labWaveformResultPath?.Trim() ?? "";

            labAudioResultPath =
                labAudioResultPath?.Trim() ?? "";
            labExaminationType =
    labExaminationType?.Trim() ?? "";

            labBodyPart =
                labBodyPart?.Trim() ?? "";

            labInterpretation =
                labInterpretation?.Trim() ?? "";

            labDicomResultPath =
                labDicomResultPath?.Trim() ?? "";

            labVideoResultPath =
                labVideoResultPath?.Trim() ?? "";

            labReportFilePath =
                labReportFilePath?.Trim() ?? "";
            labUnit =
                labUnit?.Trim() ?? "";

            labReferenceRange =
                labReferenceRange?.Trim() ?? "";

            labAbnormalFlag =
                labAbnormalFlag?.Trim() ?? "";

            labTestMethod =
                labTestMethod?.Trim() ?? "";

            labSpecimenCollectedAt =
                labSpecimenCollectedAt?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(row))
            {
                TempData["Message"] = "缺少病人編號";

                return RedirectToAction("Index");
            }

            var data = await GetAllData();

            var cadaver = data.FirstOrDefault(x =>
                string.Equals(
                    x.RowKey,
                    row,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            if (cadaver == null)
            {
                TempData["Message"] =
                    $"找不到病人資料：{row}";

                return RedirectToAction("Index");
            }

            try
            {
                string recordId =
    Guid.NewGuid().ToString("N");

                await PutLabTestRow(
                    row,
                    recordId,
                    labTestDate,
                    labTestItem,
                    labTestResult,
                    labTestItemCode,
                    labTestItemName,
                    labResultType,
                    labNumericResult,
                    labTextResult,
                    labImageResultPath,
                    labWaveformResultPath,
                    labAudioResultPath,

                    labExaminationType,
                    labBodyPart,
                    labInterpretation,
                    labDicomResultPath,
                    labVideoResultPath,
                    labReportFilePath,

                    labUnit,
                    labReferenceRange,
                    labAbnormalFlag,
                    labTestMethod,
                    labSpecimenCollectedAt
                );
                TempData["Message"] =
                    "檢驗紀錄新增成功";
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "儲存檢驗資料失敗，RowKey={RowKey}",
                    row
                );

                TempData["Message"] =
                    "儲存失敗，請確認 HBase REST API 是否正常運作";
            }

            return RedirectToAction(
                "MedicalRecords",
                new { row }
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveAdmissionRecord(
    string row,
    string admissionStartDate,
    string admissionEndDate,
    string wardType,
    string admissionReason,
    string admissionReasonCode,
    string admissionDiagnosisCode,
    string dischargeDiagnosisCode,
    string primaryDiagnosisCode,
    string secondaryDiagnosisCode,
    string hospitalCode,
    string departmentCode,
    string dischargeStatus,
    string lengthOfStay,
    string icuDays,
    string inpatientOrderCode,
    string procedureCode)
        {
            var block = RequireTeacher();

            if (block != null)
            {
                return block;
            }

            row = row?.Trim() ?? "";
            admissionStartDate = admissionStartDate?.Trim() ?? "";
            admissionEndDate = admissionEndDate?.Trim() ?? "";
            wardType = wardType?.Trim() ?? "";
            admissionReason = admissionReason?.Trim() ?? "";
            admissionReasonCode = admissionReasonCode?.Trim() ?? "";
            admissionDiagnosisCode = admissionDiagnosisCode?.Trim() ?? "";
            dischargeDiagnosisCode = dischargeDiagnosisCode?.Trim() ?? "";
            primaryDiagnosisCode = primaryDiagnosisCode?.Trim() ?? "";
            secondaryDiagnosisCode = secondaryDiagnosisCode?.Trim() ?? "";
            hospitalCode = hospitalCode?.Trim() ?? "";
            departmentCode = departmentCode?.Trim() ?? "";
            dischargeStatus = dischargeStatus?.Trim() ?? "";
            lengthOfStay = lengthOfStay?.Trim() ?? "";
            icuDays = icuDays?.Trim() ?? "";
            inpatientOrderCode = inpatientOrderCode?.Trim() ?? "";
            procedureCode = procedureCode?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(row))
            {
                TempData["Message"] = "缺少病人編號";

                return RedirectToAction("Index");
            }

            var data = await GetAllData();

            var cadaver = data.FirstOrDefault(x =>
                string.Equals(
                    x.RowKey,
                    row,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            if (cadaver == null)
            {
                TempData["Message"] =
                    $"找不到病人資料：{row}";

                return RedirectToAction("Index");
            }

            try
            {
                await PutAdmissionRow(
                    row,
                    admissionStartDate,
                    admissionEndDate,
                    wardType,
                    admissionReason,
                    admissionReasonCode,
                    admissionDiagnosisCode,
                    dischargeDiagnosisCode,
                    primaryDiagnosisCode,
                    secondaryDiagnosisCode,
                    hospitalCode,
                    departmentCode,
                    dischargeStatus,
                    lengthOfStay,
                    icuDays,
                    inpatientOrderCode,
                    procedureCode
                );

                TempData["Message"] =
                    "住院資料已儲存";
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "儲存住院資料失敗，RowKey={RowKey}",
                    row
                );

                TempData["Message"] =
                    "儲存失敗，請確認 HBase REST API 是否正常運作";
            }

            return RedirectToAction(
                "MedicalRecords",
                new { row }
            );
        }
        [HttpPost]
        public async Task<IActionResult> SaveMedicalRecord(
            string row,
            string occurredAt,
            string diagnosis,
            string bodyPart,
            string finding,
            string treatment,
            string physician,
            string note)
        {
            var block = RequireTeacher();
            if (block != null) return block;

            if (string.IsNullOrWhiteSpace(row))
            {
                TempData["Message"] = "缺少病人編號";
                return RedirectToAction("Index");
            }

            string recordId = Guid.NewGuid().ToString("N");
            string effectiveTime = string.IsNullOrWhiteSpace(occurredAt)
                ? DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
                : occurredAt;

            string fhirJson = BuildObservationFhirJson(
                row,
                recordId,
                effectiveTime,
                diagnosis,
                bodyPart,
                finding,
                treatment,
                physician,
                note
            );

            await PutMedicalRecord(row, new MedicalRecord
            {
                Id = recordId,
                ResourceType = "Observation",
                OccurredAt = effectiveTime,
                Diagnosis = diagnosis ?? "",
                BodyPart = bodyPart ?? "",
                Finding = finding ?? "",
                Treatment = treatment ?? "",
                Physician = physician ?? "",
                Note = note ?? "",
                FhirJson = fhirJson
            });

            TempData["Message"] = "病歷資料已新增";
            return RedirectToAction("MedicalRecords", new { row });
        }

        // =========================
        // 影像 / 影片查詢頁面
        // =========================
        [HttpGet]
        public async Task<IActionResult> Image(
    string row,
    string contentType = "",
    string mediaType = "",
    int mediaIndex = 0,
    string labRecordId = "")
        {
            var block = RequireLogin();

            if (block != null)
                return block;


            // =========================
            // 基本參數
            // =========================

            row =
                row?.Trim() ?? "";

            labRecordId =
    labRecordId?.Trim() ?? "";
            contentType =
                contentType?
                    .Trim()
                    .ToLowerInvariant()
                ?? "";

            mediaType =
                mediaType?
                    .Trim()
                    .ToLowerInvariant()
                ?? "";


            if (string.IsNullOrWhiteSpace(row))
            {
                TempData["Message"] =
                    "缺少病人編號。";

                return RedirectToAction("Index");
            }


            // =========================
            // 取得病人
            // =========================

            var data =
                await GetAllData();

            var item =
                data.FirstOrDefault(x =>
                    string.Equals(
                        x.RowKey,
                        row,
                        StringComparison.OrdinalIgnoreCase
                    )
                );


            if (item == null)
            {
                TempData["Message"] =
                    $"找不到病人資料：{row}";

                return RedirectToAction("Index");
            }


            ViewBag.RowKey =
                row;

            ViewBag.ContentType =
                contentType;

            ViewBag.MediaType =
                mediaType;

            ViewBag.MediaIndex =
                0;

            ViewBag.ResultCount =
                0;


            // =========================
            // 兩個選單都必須有效
            // =========================

            bool validContentType =
            contentType == "course" ||
            contentType == "medical" ||
            contentType == "pathology" ||
            contentType == "lab";

            bool validMediaType =
                mediaType == "image" ||
                mediaType == "video";

            bool hasSearch =
                validContentType
                &&
                validMediaType
                &&
                !(
                    contentType == "lab"
                    &&
                    mediaType != "video"
                );

            ViewBag.HasSearch =
                hasSearch;


            // =========================
            // 初次進頁
            // 不送任何媒體資料
            // =========================

            if (!hasSearch)
            {
                return View();
            }


            // ============================================================
            // 照片
            // ============================================================

            if (mediaType == "image")
            {
                List<string> imagePaths = new();
                List<string> imageIds = new();
                List<string> imageNotes = new();
                List<string> drawingJsonList = new();
                List<string> annotatedImagePaths = new();


                if (contentType == "course")
                {
                    imagePaths =
                        item.CourseImagePaths
                        ?? new List<string>();

                    imageIds =
                        item.CourseImageIds
                        ?? new List<string>();

                    imageNotes =
                        item.CourseImageNotes
                        ?? new List<string>();

                    drawingJsonList =
                        item.CourseDrawingJsonList
                        ?? new List<string>();

                    annotatedImagePaths =
                        item.CourseAnnotatedImagePaths
                        ?? new List<string>();
                }
                else if (
                    contentType == "medical" ||
                    contentType == "pathology")
                {
                    List<string> allPaths =
                        item.MedicalImagePaths
                        ?? new List<string>();

                    List<string> allIds =
                        item.MedicalImageIds
                        ?? new List<string>();

                    List<string> allNotes =
                        item.MedicalImageNotes
                        ?? new List<string>();

                    List<string> allDrawings =
                        item.MedicalDrawingJsonList
                        ?? new List<string>();

                    List<string> allAnnotated =
                        item.MedicalAnnotatedImagePaths
                        ?? new List<string>();

                    List<string> allExamTypes =
                        item.MedicalImageExaminationTypes
                        ?? new List<string>();

                    for (int i = 0; i < allPaths.Count; i++)
                    {
                        string examType =
                            i < allExamTypes.Count
                                ? allExamTypes[i]?.Trim() ?? ""
                                : "";

                        bool isPathology =
                            examType.Equals(
                                "Pathology",
                                StringComparison.OrdinalIgnoreCase
                            );

                        bool include =
                            contentType == "pathology"
                                ? isPathology
                                : !isPathology;

                        if (!include)
                            continue;

                        imagePaths.Add(
                            allPaths[i]
                        );

                        imageIds.Add(
                            i < allIds.Count
                                ? allIds[i]
                                : ""
                        );

                        imageNotes.Add(
                            i < allNotes.Count
                                ? allNotes[i]
                                : ""
                        );

                        drawingJsonList.Add(
                            i < allDrawings.Count
                                ? allDrawings[i]
                                : "[]"
                        );

                        annotatedImagePaths.Add(
                            i < allAnnotated.Count
                                ? allAnnotated[i]
                                : ""
                        );
                    }
                }


                int count =
                    imagePaths.Count;

                ViewBag.ResultCount =
                    count;


                if (count == 0)
                {
                    return View();
                }


                // 防止 index 超出範圍
                mediaIndex =
                    Math.Clamp(
                        mediaIndex,
                        0,
                        count - 1
                    );


                ViewBag.MediaIndex =
                    mediaIndex;


                // =========================
                // 只取目前這一張
                // =========================

                ViewBag.SelectedImagePath =
                    imagePaths[mediaIndex];


                ViewBag.SelectedImageId =
                    mediaIndex < imageIds.Count
                        ? imageIds[mediaIndex]
                        : "";


                ViewBag.SelectedImageNote =
                    mediaIndex < imageNotes.Count
                        ? imageNotes[mediaIndex]
                        : "";


                ViewBag.SelectedDrawingJson =
                    mediaIndex < drawingJsonList.Count
                        ? drawingJsonList[mediaIndex]
                        : "[]";


                ViewBag.SelectedAnnotatedImagePath =
                    mediaIndex < annotatedImagePaths.Count
                        ? annotatedImagePaths[mediaIndex]
                        : "";


                return View();
            }


            // ============================================================
            // 影片
            // ============================================================
            if (
    mediaType == "video"
    &&
    contentType == "lab"
)
            {
                List<LabTestRecord> labRecords =
                    item.LabTestRecords
                    ?? new List<LabTestRecord>();

                LabTestRecord? lab =
                    labRecords.FirstOrDefault(x =>
                        string.Equals(
                            x.Id?.Trim(),
                            labRecordId,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );

                if (lab == null)
                {
                    ViewBag.ResultCount = 0;

                    TempData["Message"] =
                        "找不到指定的檢查影片紀錄。";

                    return View();
                }

                string videoPath =
                    lab.VideoResultPath?.Trim()
                    ?? "";

                if (string.IsNullOrWhiteSpace(
                        videoPath))
                {
                    ViewBag.ResultCount = 0;

                    TempData["Message"] =
                        "此檢查紀錄沒有影片。";

                    return View();
                }

                ViewBag.ResultCount = 1;
                ViewBag.MediaIndex = 0;

                ViewBag.SelectedVideoPath =
                    videoPath;

                ViewBag.SelectedVideoNote =
                    !string.IsNullOrWhiteSpace(
                        lab.Interpretation)
                        ? lab.Interpretation
                        : GetLabDisplayName(lab);

                ViewBag.SelectedVideoCategory =
                    !string.IsNullOrWhiteSpace(
                        lab.ExaminationType)
                        ? lab.ExaminationType
                        : "檢查影片";

                ViewBag.SelectedVideoIsAlive =
                    false;

                return View();
            }
            var videoPaths =
                item.VideoPaths
                ?? new List<string>();

            var videoTypes =
                item.VideoTypes
                ?? new List<string>();

            var videoNotes =
                item.VideoNotes
                ?? new List<string>();

            var videoCategories =
                item.VideoCategories
                ?? new List<string>();

            var videoAlive =
                item.VideoIsAliveImages
                ?? new List<bool>();


            // 找出符合解剖 / 病歷類型的影片 index
            var matchedIndices =
                Enumerable
                    .Range(
                        0,
                        videoPaths.Count
                    )
                    .Where(i =>
                        i < videoTypes.Count
                        &&
                        string.Equals(
                            videoTypes[i],
                            contentType,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .ToList();


            int videoCount =
                matchedIndices.Count;

            ViewBag.ResultCount =
                videoCount;


            if (videoCount == 0)
            {
                return View();
            }


            mediaIndex =
                Math.Clamp(
                    mediaIndex,
                    0,
                    videoCount - 1
                );


            ViewBag.MediaIndex =
                mediaIndex;


            int realIndex =
                matchedIndices[mediaIndex];


            // =========================
            // 只取目前這一部
            // =========================

            ViewBag.SelectedVideoPath =
                videoPaths[realIndex];


            ViewBag.SelectedVideoNote =
                realIndex < videoNotes.Count
                    ? videoNotes[realIndex]
                    : "";


            ViewBag.SelectedVideoCategory =
                realIndex < videoCategories.Count
                    ? videoCategories[realIndex]
                    : "未分類";


            ViewBag.SelectedVideoIsAlive =
                realIndex < videoAlive.Count
                &&
                videoAlive[realIndex];


            return View();
        }


        [HttpPost]
        public async Task<IActionResult> DeleteVideo(
            [FromBody] DeleteVideoRequest req)
        {
            var block =
                RequireTeacher();

            if (block != null)
                return block;


            // =========================
            // 基本驗證
            // =========================

            if (req == null)
            {
                return BadRequest(
                    "缺少刪除影片資料。"
                );
            }

            string rowKey =
                req.RowKey?.Trim() ?? "";

            string videoPath =
                req.VideoPath?.Trim() ?? "";


            if (string.IsNullOrWhiteSpace(
                    rowKey))
            {
                return BadRequest(
                    "缺少 RowKey。"
                );
            }

            if (string.IsNullOrWhiteSpace(
                    videoPath))
            {
                return BadRequest(
                    "缺少 VideoPath。"
                );
            }


            // =========================
            // 取得目前病人資料
            // =========================

            var data =
                await GetAllData();

            var cadaver =
                data.FirstOrDefault(x =>
                    string.Equals(
                        x.RowKey,
                        rowKey,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (cadaver == null)
            {
                return BadRequest(
                    $"找不到病人資料：{rowKey}"
                );
            }


            // =========================
            // 找到影片位置
            // =========================

            int index =
                cadaver.VideoPaths
                    .FindIndex(path =>
                        string.Equals(
                            path?.Trim(),
                            videoPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );

            if (index < 0)
            {
                return BadRequest(
                    "找不到指定影片。"
                );
            }


            // =========================
            // 找影片 ID
            //
            // 現有 Model 沒有 VideoIds，
            // 所以從 HBase 原始資料重新找
            // =========================

            string videoId =
                "";

            try
            {
                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(10)
                    );

                var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        $"cadaver/{Uri.EscapeDataString(rowKey)}"
                    );

                request.Headers.Add(
                    "Accept",
                    "application/json"
                );

                var response =
                    await _client.SendAsync(
                        request,
                        cts.Token
                    );

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode(
                        500,
                        "無法讀取 HBase 影片資料。"
                    );
                }

                string json =
                    await response.Content
                        .ReadAsStringAsync();

                using JsonDocument doc =
                    JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty(
                        "Row",
                        out JsonElement rows))
                {
                    foreach (
                        var row
                        in rows.EnumerateArray())
                    {
                        if (!row.TryGetProperty(
                                "Cell",
                                out JsonElement cells))
                        {
                            continue;
                        }

                        foreach (
                            var cell
                            in cells.EnumerateArray())
                        {
                            string column =
                                Decode(
                                    cell.GetProperty(
                                        "column"
                                    ).GetString()
                                    ?? ""
                                );

                            string value =
                                Decode(
                                    cell.GetProperty(
                                        "$"
                                    ).GetString()
                                    ?? ""
                                );

                            if (!column.StartsWith(
                                    "info:video_"))
                            {
                                continue;
                            }

                            if (string.Equals(
                                    value?.Trim(),
                                    videoPath,
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                videoId =
                                    column.Replace(
                                        "info:video_",
                                        ""
                                    );

                                break;
                            }
                        }

                        if (!string.IsNullOrWhiteSpace(
                                videoId))
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "取得影片 ID 失敗。RowKey={RowKey}, VideoPath={VideoPath}",
                    rowKey,
                    videoPath
                );

                return StatusCode(
                    500,
                    "取得影片資料失敗。"
                );
            }


            if (string.IsNullOrWhiteSpace(
                    videoId))
            {
                return BadRequest(
                    "找不到影片 ID。"
                );
            }


            // =========================
            // 只刪除這支影片相關欄位
            // =========================

            var columns =
            new List<string>
           {
          $"info:video_{videoId}",
          $"info:video_type_{videoId}",
          $"info:note_{videoId}",
          $"info:category_{videoId}",
          $"info:isAlive_{videoId}"
           };


            foreach (string column in columns)
            {
                string encodedRow =
                    Uri.EscapeDataString(
                        rowKey
                    );

                string encodedColumn =
                    Uri.EscapeDataString(
                        column
                    );

                var response =
                    await _client.DeleteAsync(
                        $"cadaver/{encodedRow}/{encodedColumn}"
                    );


                // 某些附加欄位可能本來就不存在
                if (!response.IsSuccessStatusCode &&
                    response.StatusCode !=
                        System.Net.HttpStatusCode.NotFound)
                {
                    string error =
                        await response.Content
                            .ReadAsStringAsync();

                    _logger.LogError(
                        "刪除 HBase 影片欄位失敗。" +
                        " RowKey={RowKey}," +
                        " Column={Column}," +
                        " Status={Status}," +
                        " Body={Body}",
                        rowKey,
                        column,
                        response.StatusCode,
                        error
                    );

                    return StatusCode(
                        500,
                        $"刪除影片資料失敗：{column}"
                    );
                }
            }


            // =========================
            // 刪除 wwwroot 實體影片
            // =========================

            try
            {
                string relativePath =
                    videoPath
                        .TrimStart('/')
                        .Replace(
                            '/',
                            Path.DirectorySeparatorChar
                        );

                string wwwrootPath =
                    Path.GetFullPath(
                        Path.Combine(
                            Directory.GetCurrentDirectory(),
                            "wwwroot"
                        )
                    );

                string fullPath =
                    Path.GetFullPath(
                        Path.Combine(
                            wwwrootPath,
                            relativePath
                        )
                    );


                // 防止 ../ 路徑跳脫 wwwroot
                if (fullPath.StartsWith(
                        wwwrootPath,
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    System.IO.File.Exists(
                        fullPath))
                {
                    System.IO.File.Delete(
                        fullPath
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "HBase 影片資料已刪除，" +
                    "但實體影片刪除失敗。" +
                    " RowKey={RowKey}," +
                    " VideoPath={VideoPath}",
                    rowKey,
                    videoPath
                );
            }


            return Ok(
                new
                {
                    success = true,
                    rowKey,
                    videoId
                }
            );
        }

        // =========================
        // 儲存畫圖筆畫（JSON + 合成圖）
        // =========================
        [HttpPost]
        public async Task<IActionResult> SaveDrawing(
    string row,
    string imageId,
    string imageType,
    string drawingJson,
    string annotatedImageBase64)
        {
            var block =
                RequireTeacher();

            if (block != null)
                return block;


            row =
                row?.Trim() ?? "";

            imageId =
                imageId?.Trim() ?? "";

            imageType =
                imageType?.Trim()
                    .ToLowerInvariant()
                ?? "";

            drawingJson ??=
                "[]";


            if (string.IsNullOrWhiteSpace(row) ||
                string.IsNullOrWhiteSpace(imageId))
            {
                return BadRequest(
                    "缺少必要參數"
                );
            }


            // =========================
            // 決定 HBase Column Family
            // =========================

            string family;

            if (imageType == "course")
            {
                family = "course_image";
            }
            else if (
                imageType == "medical" ||
                imageType == "pathology")
            {
                family = "medical_image";
            }
            else if (imageType == "legacy")
            {
                family = "info";
            }
            else
            {
                return BadRequest(
                    "影像類型錯誤"
                );
            }


            // =========================
            // 儲存合成標註圖
            // =========================

            string annoFileName =
                "";

            if (!string.IsNullOrWhiteSpace(
                    annotatedImageBase64))
            {
                var folder =
                    Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "wwwroot",
                        "annotations"
                    );

                Directory.CreateDirectory(
                    folder
                );

                annoFileName =
                    $"{row}_{imageType}_{imageId}_anno.png";

                string b64 =
                    annotatedImageBase64.Contains(",")
                        ? annotatedImageBase64
                            .Split(',')[1]
                        : annotatedImageBase64;

                byte[] bytes =
                    Convert.FromBase64String(
                        b64
                    );

                await System.IO.File
                    .WriteAllBytesAsync(
                        Path.Combine(
                            folder,
                            annoFileName
                        ),
                        bytes
                    );
            }


            // =========================
            // 建立 HBase XML
            // =========================

            string annoCell =
                "";

            if (!string.IsNullOrWhiteSpace(
                    annoFileName))
            {
                annoCell =
                    $"<Cell column=\"{ToBase64($"{family}:anno_img_{imageId}")}\">" +
                    $"{ToBase64($"/annotations/{annoFileName}")}" +
                    "</Cell>";
            }


            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">

    <Cell column=""{ToBase64($"{family}:drawing_{imageId}")}"">
        {ToBase64(drawingJson)}
    </Cell>

    {annoCell}

  </Row>
</CellSet>";


            var content =
                new StringContent(
                    xml,
                    Encoding.UTF8,
                    "text/xml"
                );


            var response =
                await _client.PutAsync(
                    $"cadaver/{row}",
                    content
                );

            response.EnsureSuccessStatusCode();


            return Ok(
                new
                {
                    success = true,
                    imageType,
                    imageId
                }
            );
        }
        public async Task<IActionResult> TestHBaseGet()
        {
            var block = RequireTeacher();
            if (block != null) return block;

            HBaseService hbase = new HBaseService();

            string result = await hbase.GetRowAsync("patient001");

            return Content(result, "text/xml");
        }

        public async Task<IActionResult> TestHBasePut()
        {
            var block = RequireTeacher();
            if (block != null) return block;

            HBaseService hbase = new HBaseService();

            bool ok = await hbase.PutCellAsync(
                "patient002",
                "info:name",
                "MVC test patient"
            );

            if (ok)
                return Content("MVC 寫入 HBase 成功");

            return Content("MVC 寫入 HBase 失敗");
        }
        // =========================
        // 儲存標註
        // =========================
        [HttpPost]
        public async Task<IActionResult> SaveAnnotation(
            string row,
            string imagePath,
            string jsonData,
            string annotatedImageBase64)
        {
            var block = RequireTeacher();
            if (block != null) return block;

            string id = Guid.NewGuid().ToString("N");

            var folder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/annotations");
            Directory.CreateDirectory(folder);

            var fileName = $"{row}_{id}.png";
            var filePath = Path.Combine(folder, fileName);

            var base64Data = annotatedImageBase64.Split(',')[1];
            var bytes = Convert.FromBase64String(base64Data);
            await System.IO.File.WriteAllBytesAsync(filePath, bytes);

            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(row)}"">
    <Cell column=""{ToBase64($"info:anno_json_{id}")}"">{ToBase64(jsonData)}</Cell>
    <Cell column=""{ToBase64($"info:anno_img_{id}")}"">{ToBase64($"/annotations/{fileName}")}</Cell>
  </Row>
</CellSet>";

            var content = new StringContent(xml, Encoding.UTF8, "text/xml");
            await _client.PutAsync($"cadaver/{row}", content);

            return Ok();


        }
        private static string CleanJsonCodeFence(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return "";
            }

            content = content.Trim();

            if (content.StartsWith(
                    "```json",
                    StringComparison.OrdinalIgnoreCase))
            {
                content = content[7..];
            }
            else if (content.StartsWith("```"))
            {
                content = content[3..];
            }

            if (content.EndsWith("```"))
            {
                content = content[..^3];
            }

            return content.Trim();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveBodyAnnotation(
    [FromBody] SaveBodyAnnotationRequest request)
        {
            var block = RequireAdmin();

            if (block != null)
                return block;


            if (request == null)
            {
                return BadRequest(
                    new
                    {
                        success = false,
                        message = "資料不可為空"
                    }
                );
            }


            string row =
                request.Row?.Trim() ?? "";


            if (string.IsNullOrWhiteSpace(row))
            {
                return BadRequest(
                    new
                    {
                        success = false,
                        message = "缺少大體老師編號"
                    }
                );
            }


            request.Marks ??=
                new List<BodyAnnotationMark>();


            try
            {
                // ----------------------------------------------------
                // JSON
                // ----------------------------------------------------
                string json =
                    JsonSerializer.Serialize(
                        request.Marks
                    );


                string loginId =
                    HttpContext.Session.GetString(
                        "LoginId"
                    )
                    ?? "";


                string updatedAt =
                    DateTime.Now.ToString(
                        "yyyy-MM-dd HH:mm:ss"
                    );


                // ----------------------------------------------------
                // 寫入 HBase
                // teaching:body_annotation
                // ----------------------------------------------------
                string xml =
                    $@"
<CellSet>
    <Row key=""{ToBase64(row)}"">

        <Cell column=""{ToBase64("teaching:body_annotation")}"">
            {ToBase64(json)}
        </Cell>

        <Cell column=""{ToBase64("teaching:body_annotation_updated_by")}"">
            {ToBase64(loginId)}
        </Cell>

        <Cell column=""{ToBase64("teaching:body_annotation_updated_at")}"">
            {ToBase64(updatedAt)}
        </Cell>

    </Row>
</CellSet>";


                using var content =
                    new StringContent(
                        xml,
                        Encoding.UTF8,
                        "text/xml"
                    );


                string encodedRow =
                    Uri.EscapeDataString(row);


                HttpResponseMessage response =
                    await _client.PutAsync(
                        $"cadaver/{encodedRow}",
                        content
                    );


                if (!response.IsSuccessStatusCode)
                {
                    string error =
                        await response.Content
                            .ReadAsStringAsync();


                    _logger.LogError(
                        "儲存人體標記失敗。Row={Row}, Status={Status}, Error={Error}",
                        row,
                        response.StatusCode,
                        error
                    );


                    return StatusCode(
                        (int)response.StatusCode,
                        new
                        {
                            success = false,
                            message = "HBase 儲存失敗"
                        }
                    );
                }


                return Ok(
                    new
                    {
                        success = true,
                        row,
                        updatedBy = loginId,
                        updatedAt
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "儲存人體標記發生錯誤。Row={Row}",
                    row
                );


                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        message = "儲存人體標記發生錯誤"
                    }
                );
            }
        }
        [HttpGet]
        public async Task<IActionResult> GetBodyAnnotation(
    string row)
        {
            var block = RequireLogin();

            if (block != null)
                return block;


            row =
                row?.Trim() ?? "";


            if (string.IsNullOrWhiteSpace(row))
            {
                return BadRequest(
                    new
                    {
                        success = false,
                        message = "缺少大體老師編號"
                    }
                );
            }


            try
            {
                string encodedRow =
                    Uri.EscapeDataString(row);

                string encodedColumn =
                    Uri.EscapeDataString(
                        "teaching:body_annotation"
                    );


                // ----------------------------------------------------
                // HBase REST：
                // cadaver/{row}/{column}
                // ----------------------------------------------------
                var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        $"cadaver/{encodedRow}/{encodedColumn}"
                    );


                request.Headers.Add(
                    "Accept",
                    "text/xml"
                );


                HttpResponseMessage response =
                    await _client.SendAsync(
                        request
                    );


                // 尚未標記過
                if (
                    response.StatusCode ==
                    System.Net.HttpStatusCode.NotFound
                )
                {
                    return Ok(
                        new
                        {
                            success = true,
                            marks =
                                Array.Empty<BodyAnnotationMark>()
                        }
                    );
                }


                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode(
                        (int)response.StatusCode,
                        new
                        {
                            success = false,
                            message = "讀取人體標記失敗"
                        }
                    );
                }


                string xml =
                    await response.Content
                        .ReadAsStringAsync();


                byte[]? bytes =
                    ExtractHBaseCellBytes(
                        xml
                    );


                if (
                    bytes == null ||
                    bytes.Length == 0
                )
                {
                    return Ok(
                        new
                        {
                            success = true,
                            marks =
                                Array.Empty<BodyAnnotationMark>()
                        }
                    );
                }


                string json =
                    Encoding.UTF8.GetString(
                        bytes
                    );


                List<BodyAnnotationMark> marks =
                    JsonSerializer.Deserialize<
                        List<BodyAnnotationMark>
                    >(json)
                    ??
                    new List<BodyAnnotationMark>();


                return Ok(
                    new
                    {
                        success = true,
                        marks
                    }
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "讀取人體標記發生錯誤。Row={Row}",
                    row
                );


                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        message = "讀取人體標記發生錯誤"
                    }
                );
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecognizePaperMedicalRecord(
    IFormFile medicalRecordFile,
    CancellationToken cancellationToken)
        {
            if (!IsLogin())
            {
                return Unauthorized(new
                {
                    error = "登入狀態已失效，請重新登入。"
                });
            }

            if (!IsTeacher())
            {
                return StatusCode(403, new
                {
                    error = "只有老師或管理員可以辨識紙本病歷。"
                });
            }

            if (medicalRecordFile == null ||
                medicalRecordFile.Length == 0)
            {
                return BadRequest(new
                {
                    error = "請選擇紙本病歷圖片。"
                });
            }

            const long maxFileSize = 15 * 1024 * 1024;

            if (medicalRecordFile.Length > maxFileSize)
            {
                return BadRequest(new
                {
                    error = "圖片不可超過 15 MB。"
                });
            }

            string extension =
                Path.GetExtension(
                    medicalRecordFile.FileName
                ).ToLowerInvariant();

            string[] allowedExtensions =
            {
        ".jpg",
        ".jpeg",
        ".png"
    };

            if (!allowedExtensions.Contains(extension))
            {
                return BadRequest(new
                {
                    error = "目前只支援 JPG、JPEG、PNG 圖片。"
                });
            }

            try
            {
                byte[] imageBytes;

                await using (
                    var memoryStream = new MemoryStream()
                )
                {
                    await medicalRecordFile.CopyToAsync(
                        memoryStream,
                        cancellationToken
                    );

                    imageBytes = memoryStream.ToArray();
                }

                string imageBase64 =
                    Convert.ToBase64String(imageBytes);

                string recognitionPrompt = """
你是中文紙本醫療病歷辨識系統。

圖片可能包含繁體中文、簡體中文、英文縮寫、
數字、醫療符號、印刷字與手寫字。

繁體中文與簡體中文都必須辨識。
辨識結果應保留圖片原本使用的文字字形，
不可自行將簡體字轉成繁體字，
也不可自行將繁體字轉成簡體字。

請仔細辨識圖片中的手寫與印刷病歷內容。

重要規則：
1. 圖片可能包含繁體中文、簡體中文、手寫字、印刷字、英文縮寫、藥名、劑量、日期、時間、生命徵象與醫療符號。2. 必須按照圖片由上到下的順序辨識。
3. 每個日期或時間區段應拆成獨立紀錄。
4. 紅色與黑色筆跡都必須辨識，不可只讀其中一種。
5. 看不清楚的字不可自行猜測，請以「[無法辨識]」標示。
6. 不得補入圖片中不存在的姓名、病歷號碼、診斷、藥名或數值。
7. 原始文字必須保留於 rawText。
8. 日期若是民國年，例如 94-3-13，原樣保留，不要自行轉成西元。
9. 數值與單位必須盡量保留，例如 BP 183/82、體重 44.28 kg。
10. 藥名、英文縮寫與醫療術語若不確定，保留原始拼寫並加入警告。
11. confidence 為 0 到 1；文字模糊、傾斜、遮蔽或筆跡重疊時應降低。
12. 只輸出符合指定格式的 JSON，不得輸出 Markdown、解釋或前言。

輸出格式：

{
  "row": "",
  "medicalRecordNumber": "",
  "patientName": "",
  "sourceFileName": "",
  "fullRawText": "",
  "records": [
    {
      "recordDate": "",
      "recordTime": "",
      "rawText": "",
      "symptoms": "",
      "assessment": "",
      "treatment": "",
      "nursingNote": "",
      "bloodPressure": "",
      "pulse": "",
      "respiratoryRate": "",
      "temperature": "",
      "oxygenSaturation": "",
      "bodyWeight": "",
      "medications": [],
      "labFindings": [],
      "confidence": 0.0,
      "warnings": []
    }
  ],
  "globalWarnings": []
}
""";
                var ollamaRequest = new
                {
                    model = "qwen3-vl:8b-instruct",

                    messages = new object[]
                    {
        new
        {
            role = "user",
            content = recognitionPrompt,
            images = new[]
            {
                imageBase64
            }
        }
                    },

                    stream = false,

                    // 關閉推理輸出，避免 token 全部耗在 thinking
                    think = false,

                    format = "json",
                    keep_alive = "10m",

                    options = new
                    {
                        temperature = 0.0,
                        num_predict = 4000,
                        num_ctx = 8192
                    }
                };

                using HttpResponseMessage response =
                    await _ollamaClient.PostAsJsonAsync(
                        "/api/chat",
                        ollamaRequest,
                        cancellationToken
                    );

                string responseJson =
                    await response.Content.ReadAsStringAsync(
                        cancellationToken
                    );
                _logger.LogInformation(
    "紙本病歷 Ollama 原始回傳：{ResponseJson}",
    responseJson
);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "紙本病歷辨識失敗。Status={Status}, Body={Body}",
                        response.StatusCode,
                        responseJson
                    );

                    return StatusCode(503, new
                    {
                        error = "AI 圖片辨識服務目前無法使用。",
                        detail = responseJson
                    });
                }

                using JsonDocument responseDocument =
                    JsonDocument.Parse(responseJson);

                if (!responseDocument.RootElement.TryGetProperty(
                        "message",
                        out JsonElement messageElement) ||
                    !messageElement.TryGetProperty(
                        "content",
                        out JsonElement contentElement))
                {
                    return StatusCode(502, new
                    {
                        error = "AI 回傳格式不正確。"
                    });
                }

                string aiContent =
    contentElement.GetString()?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(aiContent))
                {
                    string thinking = "";

                    if (messageElement.TryGetProperty(
                            "thinking",
                            out JsonElement thinkingElement))
                    {
                        thinking =
                            thinkingElement.GetString()?.Trim() ?? "";
                    }

                    _logger.LogError(
                        "紙本病歷模型回傳空內容。ThinkingLength={ThinkingLength}, Response={Response}",
                        thinking.Length,
                        responseJson
                    );

                    return StatusCode(502, new
                    {
                        error = "AI 模型沒有回傳辨識結果。",
                        detail =
                            string.IsNullOrWhiteSpace(thinking)
                                ? "Ollama 回傳的 message.content 是空字串。請檢查模型是否支援圖片輸入。"
                                : "模型只有產生 thinking，沒有產生最終 JSON。"
                    });
                }

                aiContent =
                    CleanJsonCodeFence(aiContent);

                if (!aiContent.StartsWith("{") &&
                    !aiContent.StartsWith("["))
                {
                    _logger.LogError(
                        "紙本病歷模型回傳的內容不是 JSON。Content={Content}",
                        aiContent
                    );

                    return StatusCode(502, new
                    {
                        error = "AI 辨識結果不是 JSON。",
                        detail = aiContent.Length > 1000
                            ? aiContent[..1000]
                            : aiContent
                    });
                }

                PaperMedicalRecordRecognitionResult?
                    recognizedData;

                try
                {
                    recognizedData =
                        JsonSerializer.Deserialize
                        <PaperMedicalRecordRecognitionResult>(
                            aiContent,
                            new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            }
                        );
                }
                catch (JsonException ex)
                {
                    _logger.LogError(
                        ex,
                        "紙本病歷 JSON 解析失敗。AIContent={AIContent}",
                        aiContent
                    );

                    return StatusCode(502, new
                    {
                        error = "AI 辨識結果不是有效 JSON。",
                        detail = ex.Message,
                        rawContent = aiContent.Length > 1500
                            ? aiContent[..1500]
                            : aiContent
                    });
                }

                if (recognizedData == null)
                {
                    return StatusCode(502, new
                    {
                        error = "無法解析 AI 辨識結果。"
                    });
                }

                recognizedData.SourceFileName =
                    Path.GetFileName(
                        medicalRecordFile.FileName
                    );

                recognizedData.Records ??=
                    new List<PaperMedicalRecordEntry>();

                recognizedData.GlobalWarnings ??=
                    new List<string>();

                return Ok(new
                {
                    success = true,
                    data = recognizedData
                });
            }
            catch (JsonException ex)
            {
                _logger.LogError(
                    ex,
                    "紙本病歷辨識 JSON 解析失敗。"
                );

                return StatusCode(502, new
                {
                    error = "AI 辨識結果不是有效 JSON。",
                    detail = ex.Message
                });
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                return StatusCode(504, new
                {
                    error = "紙本病歷辨識逾時。"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "紙本病歷辨識發生錯誤。"
                );

                return StatusCode(500, new
                {
                    error = "紙本病歷辨識失敗。",
                    detail = ex.Message
                });
            }
        }

        [HttpGet]
        public IActionResult BackupManagement()
        {
            var block = RequireAdmin();

            if (block != null)
                return block;

            return View();
        }
        // =========================
        // 備份：HBase 資料表
        // =========================

        private static readonly string[] BackupHBaseTables =
        {
    "cadaver",
    "surgical_table",
    "class_schedule",
    "student_course_note"
};
        private string GetBackupTempDirectory()
        {
            string directory =
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "App_Data",
                    "backup-temp"
                );

            Directory.CreateDirectory(directory);

            return directory;
        }


        private string GetRestoreTempDirectory()
        {
            string directory =
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "App_Data",
                    "restore-temp"
                );

            Directory.CreateDirectory(directory);

            return directory;
        }


        private void SafeDeleteFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "無法刪除暫存檔 {Path}",
                    path
                );
            }
        }

        private async Task<string> GetRawHBaseTableJson(
            string tableName)
        {
            using var cts =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(60)
                );

            var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{tableName}/*"
                );

            request.Headers.Add(
                "Accept",
                "application/json"
            );

            var response =
                await _client.SendAsync(
                    request,
                    cts.Token
                );

            // 空表可能沒有任何 Row
            if (response.StatusCode ==
                System.Net.HttpStatusCode.NotFound)
            {
                return "{\"Row\":[]}";
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"讀取 HBase 資料表 {tableName} 失敗，" +
                    $"HTTP {(int)response.StatusCode}"
                );
            }

            string json =
                await response.Content
                    .ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(json))
            {
                return "{\"Row\":[]}";
            }

            return json;
        }
        private void AddDirectoryToZip(
            ZipArchive archive,
            string sourceDirectory,
            string zipRoot)
        {
            if (!Directory.Exists(sourceDirectory))
                return;

            foreach (
                string filePath
                in Directory.EnumerateFiles(
                    sourceDirectory,
                    "*",
                    SearchOption.AllDirectories))
            {
                string relativePath =
                    Path.GetRelativePath(
                        sourceDirectory,
                        filePath
                    );

                string zipPath =
                    Path.Combine(
                        zipRoot,
                        relativePath
                    )
                    .Replace(
                        Path.DirectorySeparatorChar,
                        '/'
                    );

                // JPG / PNG / MP4 本身已經是壓縮格式，
                // 不需要再浪費大量 CPU 壓縮。
                string extension =
                    Path.GetExtension(filePath)
                        .ToLowerInvariant();

                CompressionLevel level =
                    extension == ".jpg" ||
                    extension == ".jpeg" ||
                    extension == ".png" ||
                    extension == ".mp4"
                        ? CompressionLevel.NoCompression
                        : CompressionLevel.Optimal;

                ZipArchiveEntry entry =
                    archive.CreateEntry(
                        zipPath,
                        level
                    );

                using Stream destination =
                    entry.Open();

                using FileStream source =
                    new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        1024 * 1024,
                        FileOptions.SequentialScan
                    );

                source.CopyTo(
                    destination,
                    1024 * 1024
                );
            }
        }
        private async Task CreateSystemBackupZip(
    string destinationPath,
    string createdBy,
    string role)
        {
            await using var fileStream =
                new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1024 * 1024,
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan
                );

            using var archive =
                new ZipArchive(
                    fileStream,
                    ZipArchiveMode.Create,
                    true
                );


            // =========================
            // 1. HBase
            // =========================

            foreach (
                string tableName
                in BackupHBaseTables)
            {
                await WriteHBaseTableToZip(
                    archive,
                    tableName
                );
            }


            // =========================
            // 2. users.json
            // =========================

            EnsureUserFile();

            if (System.IO.File.Exists(
                    UserFilePath))
            {
                ZipArchiveEntry usersEntry =
                    archive.CreateEntry(
                        "database/users.json",
                        CompressionLevel.Optimal
                    );

                await using Stream zipStream =
                    usersEntry.Open();

                await using FileStream source =
                    new FileStream(
                        UserFilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        1024 * 1024,
                        FileOptions.Asynchronous |
                        FileOptions.SequentialScan
                    );

                await source.CopyToAsync(
                    zipStream,
                    1024 * 1024
                );
            }


            // =========================
            // 3. 實體圖片 / 影片
            // =========================

            string wwwroot =
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot"
                );

            AddDirectoryToZip(
                archive,
                Path.Combine(
                    wwwroot,
                    "medical-images"
                ),
                "files/wwwroot/medical-images"
            );

            AddDirectoryToZip(
                archive,
                Path.Combine(
                    wwwroot,
                    "course-images"
                ),
                "files/wwwroot/course-images"
            );

            AddDirectoryToZip(
                archive,
                Path.Combine(
                    wwwroot,
                    "videos"
                ),
                "files/wwwroot/videos"
            );

            AddDirectoryToZip(
                archive,
                Path.Combine(
                    wwwroot,
                    "annotations"
                ),
                "files/wwwroot/annotations"
            );


            // =========================
            // 4. manifest
            // =========================

            var manifest = new
            {
                BackupType =
                    "PathologyTeachingFullBackup",

                Version = 4,

                CreatedAt =
                    DateTime.Now.ToString(
                        "yyyy-MM-ddTHH:mm:ss"
                    ),

                CreatedBy =
                    createdBy,

                Role =
                    role,

                HBaseTables =
                    BackupHBaseTables,

                IncludesUsers = true,

                IncludesFiles = true,

                StorageMode =
                    "DiskStreaming",

                HBaseFormat =
                    "XmlStreaming",

                IncludedDirectories =
                    new[]
                    {
                "wwwroot/medical-images",
                "wwwroot/course-images",
                "wwwroot/videos",
                "wwwroot/annotations"
                    }
            };

            string manifestJson =
                JsonSerializer.Serialize(
                    manifest,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    }
                );

            ZipArchiveEntry manifestEntry =
                archive.CreateEntry(
                    "manifest.json",
                    CompressionLevel.Optimal
                );

            await using Stream manifestStream =
                manifestEntry.Open();

            await using var writer =
                new StreamWriter(
                    manifestStream,
                    new UTF8Encoding(false)
                );

            await writer.WriteAsync(
                manifestJson
            );
        }
        private async Task WriteHBaseTableToZip(
    ZipArchive archive,
    string tableName)
        {
            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{tableName}/*"
                );

            request.Headers.TryAddWithoutValidation(
                "Accept",
                "text/xml"
            );

            using HttpResponseMessage response =
                await _client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead
                );

            ZipArchiveEntry entry =
                archive.CreateEntry(
                    $"database/{tableName}.xml",
                    CompressionLevel.Optimal
                );

            await using Stream destination =
                entry.Open();

            if (response.StatusCode ==
                System.Net.HttpStatusCode.NotFound)
            {
                byte[] empty =
                    Encoding.UTF8.GetBytes(
                        "<CellSet></CellSet>"
                    );

                await destination.WriteAsync(
                    empty
                );

                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"讀取 HBase 資料表 {tableName} 失敗，" +
                    $"HTTP {(int)response.StatusCode}"
                );
            }

            await using Stream source =
                await response.Content
                    .ReadAsStreamAsync();

            await source.CopyToAsync(
                destination,
                1024 * 1024
            );
        }
        // =========================
        // 管理員：下載完整系統資料備份
        // =========================

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult>
            DownloadDatabaseBackup()
        {
            var block = RequireAdmin();

            if (block != null)
                return block;
            string tempFilePath = "";

            try
            {
                string fileName =
                    "pathology_full_backup_" +
                    DateTime.Now.ToString(
                        "yyyyMMdd_HHmmss"
                    ) +
                    ".zip";

                tempFilePath =
                    Path.Combine(
                        GetBackupTempDirectory(),
                        Guid.NewGuid()
                            .ToString("N") +
                        ".zip"
                    );

                string loginId =
                    HttpContext.Session
                        .GetString("LoginId")
                    ?? "";

                string role =
                    HttpContext.Session
                        .GetString("Role")
                    ?? "";

                await CreateSystemBackupZip(
                    tempFilePath,
                    loginId,
                    role
                );

                FileStream stream =
                    new FileStream(
                        tempFilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        1024 * 1024,
                        FileOptions.Asynchronous |
                        FileOptions.SequentialScan |
                        FileOptions.DeleteOnClose
                    );

                return File(
                    stream,
                    "application/zip",
                    fileName,
                    enableRangeProcessing: false
                );
            }
            catch (Exception ex)
            {
                SafeDeleteFile(
                    tempFilePath
                );

                _logger.LogError(
                    ex,
                    "建立完整系統資料備份失敗"
                );

                TempData["Message"] =
                    "建立備份失敗：" +
                    ex.Message;

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "BackupManagement"
                );
            }
        }
        private async Task<string>
    ReadZipEntryText(
        ZipArchiveEntry entry)
        {
            await using Stream stream =
                entry.Open();

            using var reader =
                new StreamReader(
                    stream,
                    Encoding.UTF8,
                    true
                );

            return await reader
                .ReadToEndAsync();
        }
        // =========================
        // 恢復：清除 HBase 資料表中的所有 Row
        // =========================

        private async Task ClearHBaseTableRows(
            string tableName)
        {
            string json =
                await GetRawHBaseTableJson(
                    tableName
                );

            if (string.IsNullOrWhiteSpace(json))
                return;

            using JsonDocument doc =
                JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty(
                    "Row",
                    out JsonElement rows))
            {
                return;
            }

            List<string> rowKeys =
                new List<string>();

            foreach (
                JsonElement row
                in rows.EnumerateArray())
            {
                if (!row.TryGetProperty(
                        "key",
                        out JsonElement keyElement))
                {
                    continue;
                }

                string keyBase64 =
                    keyElement.GetString() ?? "";

                if (string.IsNullOrWhiteSpace(
                        keyBase64))
                {
                    continue;
                }

                string rowKey =
                    Decode(keyBase64);

                if (!string.IsNullOrWhiteSpace(
                        rowKey))
                {
                    rowKeys.Add(rowKey);
                }
            }

            foreach (string rowKey in rowKeys)
            {
                string encodedRowKey =
                    Uri.EscapeDataString(rowKey);

                var response =
                    await _client.DeleteAsync(
                        $"{tableName}/{encodedRowKey}"
                    );

                if (!response.IsSuccessStatusCode &&
                    response.StatusCode !=
                        System.Net.HttpStatusCode.NotFound)
                {
                    throw new Exception(
                        $"清除資料表 {tableName} 的 Row " +
                        $"{rowKey} 失敗，HTTP " +
                        $"{(int)response.StatusCode}"
                    );
                }
            }
        }
        private async Task ValidateSystemBackupZip(
    string zipPath)
        {
            await using var fileStream =
                new FileStream(
                    zipPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    1024 * 1024,
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan
                );

            using var archive =
                new ZipArchive(
                    fileStream,
                    ZipArchiveMode.Read,
                    false
                );


            ZipArchiveEntry? manifestEntry =
                archive.GetEntry(
                    "manifest.json"
                );

            if (manifestEntry == null)
            {
                throw new Exception(
                    "備份檔缺少 manifest.json。"
                );
            }

            string manifestJson =
                await ReadZipEntryText(
                    manifestEntry
                );

            using JsonDocument document =
                JsonDocument.Parse(
                    manifestJson
                );

            JsonElement root =
                document.RootElement;

            string backupType =
                root.TryGetProperty(
                    "BackupType",
                    out JsonElement type)
                    ? type.GetString() ?? ""
                    : "";

            int version =
                root.TryGetProperty(
                    "Version",
                    out JsonElement ver)
                    ? ver.GetInt32()
                    : 0;

            if (backupType !=
                    "PathologyTeachingFullBackup"
                ||
                version != 4)
            {
                throw new Exception(
                    "這不是目前 Version 4 的完整系統備份檔。"
                );
            }


            foreach (
                string tableName
                in BackupHBaseTables)
            {
                ZipArchiveEntry? entry =
                    archive.GetEntry(
                        $"database/{tableName}.xml"
                    );

                if (entry == null)
                {
                    throw new Exception(
                        $"備份檔缺少 database/{tableName}.xml。"
                    );
                }

                // 只確認 XML 可以正常讀取
                await using Stream xmlStream =
                    entry.Open();

                XmlReaderSettings settings =
                    new XmlReaderSettings
                    {
                        Async = true,
                        DtdProcessing =
                            DtdProcessing.Prohibit
                    };

                using XmlReader reader =
                    XmlReader.Create(
                        xmlStream,
                        settings
                    );

                bool cellSetFound = false;

                while (await reader.ReadAsync())
                {
                    if (reader.NodeType ==
                            XmlNodeType.Element
                        &&
                        reader.LocalName ==
                            "CellSet")
                    {
                        cellSetFound = true;
                        break;
                    }
                }

                if (!cellSetFound)
                {
                    throw new Exception(
                        $"database/{tableName}.xml 格式錯誤。"
                    );
                }
            }


            ZipArchiveEntry? usersEntry =
                archive.GetEntry(
                    "database/users.json"
                );

            if (usersEntry == null)
            {
                throw new Exception(
                    "備份檔缺少 database/users.json。"
                );
            }

            string usersJson =
                await ReadZipEntryText(
                    usersEntry
                );

            List<UserAccount>? users =
                JsonSerializer.Deserialize<
                    List<UserAccount>
                >(usersJson);

            if (users == null)
            {
                throw new Exception(
                    "users.json 格式錯誤。"
                );
            }

            if (!users.Any(x =>
                    x.Role == "Admin"))
            {
                throw new Exception(
                    "備份中不存在管理員帳號，停止恢復。"
                );
            }
        }
        private async Task ClearHBaseTableRowsStreaming(
            string tableName)
        {
            string tempKeyFile =
                Path.Combine(
                    Path.GetTempPath(),
                    "hbase_clear_" +
                    Guid.NewGuid().ToString("N") +
                    ".txt"
                );

            try
            {
                // =========================
                // 1. 先完整讀取 RowKey
                // 不在掃描期間修改 HBase
                // =========================

                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        $"{tableName}/*"
                    );

                request.Headers.TryAddWithoutValidation(
                    "Accept",
                    "text/xml"
                );

                using HttpResponseMessage response =
                    await _client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead
                    );

                if (response.StatusCode ==
                    System.Net.HttpStatusCode.NotFound)
                {
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception(
                        $"讀取資料表 {tableName} 失敗，HTTP " +
                        $"{(int)response.StatusCode}"
                    );
                }

                await using Stream source =
                    await response.Content
                        .ReadAsStreamAsync();

                XmlReaderSettings settings =
                    new XmlReaderSettings
                    {
                        Async = true,
                        DtdProcessing =
                            DtdProcessing.Prohibit,

                        IgnoreWhitespace = true,
                        IgnoreComments = true
                    };

                using XmlReader reader =
                    XmlReader.Create(
                        source,
                        settings
                    );

                await using (
                    var writer =
                        new StreamWriter(
                            tempKeyFile,
                            false,
                            new UTF8Encoding(false)
                        )
                )
                {
                    while (await reader.ReadAsync())
                    {
                        if (reader.NodeType !=
                                XmlNodeType.Element
                            ||
                            reader.LocalName !=
                                "Row")
                        {
                            continue;
                        }

                        string rowKeyBase64 =
                            reader.GetAttribute("key")
                            ?? "";

                        if (string.IsNullOrWhiteSpace(
                                rowKeyBase64))
                        {
                            continue;
                        }

                        // 暫存 Base64，
                        // 避免 RowKey 本身含特殊字元
                        await writer.WriteLineAsync(
                            rowKeyBase64
                        );
                    }
                }


                // =========================
                // 2. 完整掃描結束後
                // 再逐筆 DELETE
                // =========================

                using var keyReader =
                    new StreamReader(
                        tempKeyFile,
                        Encoding.UTF8,
                        true
                    );

                string? rowKeyBase64Line;

                while (
                    (rowKeyBase64Line =
                        await keyReader.ReadLineAsync())
                    != null
                )
                {
                    if (string.IsNullOrWhiteSpace(
                            rowKeyBase64Line))
                    {
                        continue;
                    }

                    string rowKey =
                        Encoding.UTF8.GetString(
                            Convert.FromBase64String(
                                rowKeyBase64Line
                            )
                        );

                    using HttpResponseMessage
                        deleteResponse =
                            await _client.DeleteAsync(
                                $"{tableName}/" +
                                Uri.EscapeDataString(
                                    rowKey
                                )
                            );

                    if (!deleteResponse
                            .IsSuccessStatusCode
                        &&
                        deleteResponse.StatusCode !=
                            System.Net.HttpStatusCode.NotFound)
                    {
                        throw new Exception(
                            $"刪除資料表 {tableName} Row {rowKey} 失敗，" +
                            $"HTTP {(int)deleteResponse.StatusCode}"
                        );
                    }
                }
            }
            finally
            {
                if (System.IO.File.Exists(
                        tempKeyFile))
                {
                    try
                    {
                        System.IO.File.Delete(
                            tempKeyFile
                        );
                    }
                    catch
                    {
                        // 暫存檔清除失敗不影響 Restore
                    }
                }
            }
        }
        private async Task<int> CountBackupHBaseRows(
    ZipArchiveEntry entry)
        {
            await using Stream source =
                entry.Open();

            XmlReaderSettings settings =
                new XmlReaderSettings
                {
                    Async = true,
                    DtdProcessing =
                        DtdProcessing.Prohibit,

                    IgnoreWhitespace = true,
                    IgnoreComments = true
                };

            using XmlReader reader =
                XmlReader.Create(
                    source,
                    settings
                );

            int count = 0;

            while (await reader.ReadAsync())
            {
                if (reader.NodeType ==
                        XmlNodeType.Element
                    &&
                    reader.LocalName ==
                        "Row")
                {
                    count++;
                }
            }

            return count;
        }
        private async Task<int> VerifyBackupHBaseRowsExist(
    ZipArchiveEntry entry,
    string tableName)
        {
            await using Stream source =
                entry.Open();

            XmlReaderSettings settings =
                new XmlReaderSettings
                {
                    Async = true,
                    DtdProcessing =
                        DtdProcessing.Prohibit,

                    IgnoreWhitespace = true,
                    IgnoreComments = true
                };

            using XmlReader reader =
                XmlReader.Create(
                    source,
                    settings
                );

            int verifiedRows = 0;

            while (await reader.ReadAsync())
            {
                if (reader.NodeType !=
                        XmlNodeType.Element
                    ||
                    reader.LocalName !=
                        "Row")
                {
                    continue;
                }

                string rowKeyBase64 =
                    reader.GetAttribute("key")
                    ?? "";

                if (string.IsNullOrWhiteSpace(
                        rowKeyBase64))
                {
                    throw new Exception(
                        $"驗證資料表 {tableName} 時發現沒有 key 的 Row。"
                    );
                }

                string rowKey;

                try
                {
                    rowKey =
                        Encoding.UTF8.GetString(
                            Convert.FromBase64String(
                                rowKeyBase64
                            )
                        );
                }
                catch (Exception ex)
                {
                    throw new Exception(
                        $"驗證資料表 {tableName} 時發現無效的 RowKey Base64。",
                        ex
                    );
                }


                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        $"{tableName}/" +
                        Uri.EscapeDataString(
                            rowKey
                        )
                    );

                request.Headers.TryAddWithoutValidation(
                    "Accept",
                    "text/xml"
                );

                using HttpResponseMessage response =
                    await _client.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead
                    );


                if (response.StatusCode ==
                    System.Net.HttpStatusCode.NotFound)
                {
                    throw new Exception(
                        $"Restore 驗證失敗：資料表 {tableName} " +
                        $"找不到 RowKey「{rowKey}」。"
                    );
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception(
                        $"Restore 驗證資料表 {tableName} " +
                        $"RowKey「{rowKey}」失敗，HTTP " +
                        $"{(int)response.StatusCode}。"
                    );
                }


                verifiedRows++;

                if (verifiedRows % 25 == 0)
                {
                    _logger.LogInformation(
                        "Restore 驗證：{TableName}，已確認 {Rows} Rows",
                        tableName,
                        verifiedRows
                    );
                }
            }

            return verifiedRows;
        }
        private async Task VerifyRestoredHBaseTables(
    ZipArchive archive)
        {
            foreach (
                string tableName
                in BackupHBaseTables)
            {
                ZipArchiveEntry? entry =
                    archive.GetEntry(
                        $"database/{tableName}.xml"
                    );

                if (entry == null)
                {
                    throw new Exception(
                        $"驗證 Restore 時找不到 database/{tableName}.xml。"
                    );
                }

                int expectedRows =
                    await CountBackupHBaseRows(
                        entry
                    );

                int verifiedRows =
                    await VerifyBackupHBaseRowsExist(
                        entry,
                        tableName
                    );

                _logger.LogInformation(
                    "Restore 驗證完成：{TableName}，備份 {Expected} Rows，已確認 {Verified} Rows",
                    tableName,
                    expectedRows,
                    verifiedRows
                );

                if (expectedRows != verifiedRows)
                {
                    throw new Exception(
                        $"Restore 驗證失敗：資料表 {tableName} " +
                        $"備份為 {expectedRows} Rows，" +
                        $"實際確認 {verifiedRows} Rows。"
                    );
                }
            }
        }
        private async Task RestoreHBaseTableFromXml(
    ZipArchiveEntry entry,
    string tableName)
        {
            await using Stream source =
                entry.Open();

            XmlReaderSettings settings =
                new XmlReaderSettings
                {
                    Async = true,
                    DtdProcessing =
                        DtdProcessing.Prohibit,

                    IgnoreWhitespace = true,
                    IgnoreComments = true
                };

            using XmlReader reader =
                XmlReader.Create(
                    source,
                    settings
                );

            int restoredRows = 0;

            if (!await reader.ReadAsync())
            {
                _logger.LogInformation(
                    "HBase Restore 完成：{TableName}，共 0 Rows",
                    tableName
                );

                return;
            }

            while (!reader.EOF)
            {
                if (reader.NodeType ==
                        XmlNodeType.Element
                    &&
                    reader.LocalName ==
                        "Row")
                {
                    XElement rowElement =
                        (XElement)
                        await XNode.ReadFromAsync(
                            reader,
                            CancellationToken.None
                        );

                    string rowKeyBase64 =
                        rowElement
                            .Attribute("key")
                            ?.Value
                        ?? "";

                    if (string.IsNullOrWhiteSpace(
                            rowKeyBase64))
                    {
                        throw new Exception(
                            $"資料表 {tableName} 發現沒有 key 的 Row。"
                        );
                    }

                    string rowKey;

                    try
                    {
                        rowKey =
                            Encoding.UTF8.GetString(
                                Convert.FromBase64String(
                                    rowKeyBase64
                                )
                            );
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(
                            $"資料表 {tableName} 發現無效的 RowKey Base64。",
                            ex
                        );
                    }

                    // Restore 時移除備份內舊 timestamp，
                    // 讓 HBase 自動產生新的 timestamp。
                    foreach (
                        XElement cell
                        in rowElement
                            .Descendants()
                            .Where(x =>
                                x.Name.LocalName ==
                                "Cell")
                    )
                    {
                        XAttribute? timestamp =
                            cell.Attribute(
                                "timestamp"
                            );

                        timestamp?.Remove();
                    }

                    XElement cellSet =
                        new XElement(
                            "CellSet",
                            rowElement
                        );

                    string xml =
                        cellSet.ToString(
                            SaveOptions.DisableFormatting
                        );

                    using var content =
                        new StringContent(
                            xml,
                            Encoding.UTF8,
                            "text/xml"
                        );

                    using HttpResponseMessage response =
                        await _client.PutAsync(
                            $"{tableName}/" +
                            Uri.EscapeDataString(
                                rowKey
                            ),
                            content
                        );

                    if (!response.IsSuccessStatusCode)
                    {
                        string errorText =
                            await response.Content
                                .ReadAsStringAsync();

                        throw new Exception(
                            $"恢復資料表 {tableName} Row {rowKey} 失敗，" +
                            $"HTTP {(int)response.StatusCode}，" +
                            errorText
                        );
                    }
                    bool rowVisible = false;

                    for (int verifyAttempt = 1;
                         verifyAttempt <= 5;
                         verifyAttempt++)
                    {
                        using var verifyRequest =
                            new HttpRequestMessage(
                                HttpMethod.Get,
                                $"{tableName}/" +
                                Uri.EscapeDataString(rowKey)
                            );

                        verifyRequest.Headers
                            .TryAddWithoutValidation(
                                "Accept",
                                "text/xml"
                            );

                        using HttpResponseMessage verifyResponse =
                            await _client.SendAsync(
                                verifyRequest,
                                HttpCompletionOption.ResponseHeadersRead
                            );

                        if (verifyResponse.IsSuccessStatusCode)
                        {
                            rowVisible = true;
                            break;
                        }

                        if (verifyResponse.StatusCode !=
                            System.Net.HttpStatusCode.NotFound)
                        {
                            throw new Exception(
                                $"恢復資料表 {tableName} Row {rowKey} 後驗證失敗，" +
                                $"HTTP {(int)verifyResponse.StatusCode}。"
                            );
                        }

                        await Task.Delay(100);

                        // 如果暫時查不到，重新 PUT 同一筆。
                        if (verifyAttempt < 5)
                        {
                            using var retryContent =
                                new StringContent(
                                    xml,
                                    Encoding.UTF8,
                                    "text/xml"
                                );

                            using HttpResponseMessage retryResponse =
                                await _client.PutAsync(
                                    $"{tableName}/" +
                                    Uri.EscapeDataString(rowKey),
                                    retryContent
                                );

                            if (!retryResponse.IsSuccessStatusCode)
                            {
                                throw new Exception(
                                    $"重新恢復資料表 {tableName} Row {rowKey} 失敗，" +
                                    $"HTTP {(int)retryResponse.StatusCode}。"
                                );
                            }
                        }
                    }

                    if (!rowVisible)
                    {
                        throw new Exception(
                            $"恢復資料表 {tableName} Row {rowKey} 後，" +
                            "連續 5 次仍無法讀取。"
                        );
                    }

                    restoredRows++;
                    restoredRows++;

                    if (restoredRows % 25 == 0)
                    {
                        _logger.LogInformation(
                            "HBase Restore：{TableName}，已完成 {Rows} Rows",
                            tableName,
                            restoredRows
                        );
                    }

                    continue;
                }

                await reader.ReadAsync();
            }

            _logger.LogInformation(
                "HBase Restore 完成：{TableName}，共 {Rows} Rows",
                tableName,
                restoredRows
            );
        }
        private async Task ApplySystemBackupZip(
    string zipPath)
        {
            await using var fileStream =
                new FileStream(
                    zipPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    1024 * 1024,
                    FileOptions.Asynchronous |
                    FileOptions.SequentialScan
                );

            using var archive =
                new ZipArchive(
                    fileStream,
                    ZipArchiveMode.Read,
                    false
                );

            // =========================
            // 1. 清空 HBase
            // =========================

            foreach (
                string tableName
                in BackupHBaseTables)
            {
                await ClearHBaseTableRowsStreaming(
                    tableName
                );
            }


            // DELETE 完成後稍作等待，
            // 再讓 HBase 使用伺服器目前時間寫入新 Cell。
            await Task.Delay(2000);


            // =========================
            // 2. Streaming 恢復 HBase
            // =========================

            foreach (
                string tableName
                in BackupHBaseTables)
            {
                ZipArchiveEntry? entry =
                    archive.GetEntry(
                        $"database/{tableName}.xml"
                    );

                if (entry == null)
                {
                    throw new Exception(
                        $"缺少 database/{tableName}.xml。"
                    );
                }

                await RestoreHBaseTableFromXml(
                    entry,
                    tableName
                );
            }


            // =========================
            // 3. 驗證 HBase Restore
            // =========================

            await VerifyRestoredHBaseTables(
                archive
            );

            // =========================
            // 4. users.json
            // =========================

            ZipArchiveEntry? usersEntry =
                archive.GetEntry(
                    "database/users.json"
                );

            if (usersEntry == null)
            {
                throw new Exception(
                    "缺少 database/users.json。"
                );
            }

            string usersJson =
                await ReadZipEntryText(
                    usersEntry
                );

            string? usersDirectory =
                Path.GetDirectoryName(
                    UserFilePath
                );

            if (!string.IsNullOrWhiteSpace(
                    usersDirectory))
            {
                Directory.CreateDirectory(
                    usersDirectory
                );
            }

            await System.IO.File
                .WriteAllTextAsync(
                    UserFilePath,
                    usersJson,
                    new UTF8Encoding(false)
                );


            // =========================
            // 5. 清除目前媒體
            // =========================

            string wwwroot =
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot"
                );

            string[] mediaDirectories =
            {
        "medical-images",
        "course-images",
        "videos",
        "annotations"
    };

            foreach (
                string name
                in mediaDirectories)
            {
                string path =
                    Path.Combine(
                        wwwroot,
                        name
                    );

                if (Directory.Exists(path))
                {
                    Directory.Delete(
                        path,
                        true
                    );
                }

                Directory.CreateDirectory(
                    path
                );
            }


            // =========================
            // 6. Streaming 恢復檔案
            // =========================

            await RestoreZipDirectory(
                archive,
                "files/wwwroot/medical-images",
                Path.Combine(
                    wwwroot,
                    "medical-images"
                )
            );

            await RestoreZipDirectory(
                archive,
                "files/wwwroot/course-images",
                Path.Combine(
                    wwwroot,
                    "course-images"
                )
            );

            await RestoreZipDirectory(
                archive,
                "files/wwwroot/videos",
                Path.Combine(
                    wwwroot,
                    "videos"
                )
            );

            await RestoreZipDirectory(
                archive,
                "files/wwwroot/annotations",
                Path.Combine(
                    wwwroot,
                    "annotations"
                )
            );
        }
        // =========================
        // 恢復：將原始 HBase JSON 寫回
        // =========================

        private async Task RestoreHBaseTableFromJson(
            string tableName,
            string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;

            using JsonDocument doc =
                JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty(
                    "Row",
                    out JsonElement rows))
            {
                return;
            }

            foreach (
                JsonElement row
                in rows.EnumerateArray())
            {
                if (!row.TryGetProperty(
                        "key",
                        out JsonElement keyElement))
                {
                    continue;
                }

                string rowKeyBase64 =
                    keyElement.GetString() ?? "";

                if (string.IsNullOrWhiteSpace(
                        rowKeyBase64))
                {
                    continue;
                }

                string rowKey =
                    Decode(rowKeyBase64);

                if (!row.TryGetProperty(
                        "Cell",
                        out JsonElement cells))
                {
                    continue;
                }

                StringBuilder xml =
                    new StringBuilder();

                xml.Append("<CellSet>");

                xml.Append(
                    $"<Row key=\"{rowKeyBase64}\">"
                );

                foreach (
                    JsonElement cell
                    in cells.EnumerateArray())
                {
                    string column =
                        cell.TryGetProperty(
                            "column",
                            out JsonElement columnElement)
                            ? columnElement.GetString() ?? ""
                            : "";

                    string value =
                        cell.TryGetProperty(
                            "$",
                            out JsonElement valueElement)
                            ? valueElement.GetString() ?? ""
                            : "";

                    if (string.IsNullOrWhiteSpace(
                            column))
                    {
                        continue;
                    }

                    xml.Append(
                        $"<Cell column=\"{column}\">" +
                        $"{value}" +
                        "</Cell>"
                    );
                }

                xml.Append("</Row>");
                xml.Append("</CellSet>");

                using var content =
                    new StringContent(
                        xml.ToString(),
                        Encoding.UTF8,
                        "text/xml"
                    );

                string encodedRowKey =
                    Uri.EscapeDataString(rowKey);

                var response =
                    await _client.PutAsync(
                        $"{tableName}/{encodedRowKey}",
                        content
                    );

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception(
                        $"恢復資料表 {tableName} 的 Row " +
                        $"{rowKey} 失敗，HTTP " +
                        $"{(int)response.StatusCode}"
                    );
                }
            }
        }
        // =========================
        // 恢復：wwwroot 實體檔案
        // =========================
        private async Task RestoreZipDirectory(
            ZipArchive archive,
            string zipPrefix,
            string targetDirectory)
        {
            Directory.CreateDirectory(
                targetDirectory
            );

            string normalizedTarget =
                Path.GetFullPath(
                    targetDirectory
                );

            if (!normalizedTarget.EndsWith(
                    Path.DirectorySeparatorChar))
            {
                normalizedTarget +=
                    Path.DirectorySeparatorChar;
            }

            string normalizedPrefix =
                zipPrefix
                    .Replace('\\', '/')
                    .TrimEnd('/') + "/";

            foreach (
                ZipArchiveEntry entry
                in archive.Entries)
            {
                string entryName =
                    entry.FullName
                        .Replace('\\', '/');

                if (!entryName.StartsWith(
                        normalizedPrefix,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relativePath =
                    entryName.Substring(
                        normalizedPrefix.Length
                    );

                if (string.IsNullOrWhiteSpace(
                        relativePath))
                {
                    continue;
                }

                // ZIP 內的資料夾 entry
                if (entryName.EndsWith("/"))
                {
                    continue;
                }

                string destinationPath =
                    Path.GetFullPath(
                        Path.Combine(
                            targetDirectory,
                            relativePath.Replace(
                                '/',
                                Path.DirectorySeparatorChar
                            )
                        )
                    );

                // 防止 ZIP 路徑跳脫
                if (!destinationPath.StartsWith(
                        normalizedTarget,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        "備份檔包含不安全的檔案路徑。"
                    );
                }

                string? destinationDirectory =
                    Path.GetDirectoryName(
                        destinationPath
                    );

                if (!string.IsNullOrWhiteSpace(
                        destinationDirectory))
                {
                    Directory.CreateDirectory(
                        destinationDirectory
                    );
                }

                await using var sourceStream =
                    entry.Open();

                await using var destinationStream =
                    new FileStream(
                        destinationPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None
                    );

                await sourceStream.CopyToAsync(
                    destinationStream
                );
            }
        }
        // =========================
        // 管理員：恢復完整系統資料備份
        // =========================

        [HttpPost]
        [ValidateAntiForgeryToken]
        [DisableRequestSizeLimit]
        [RequestFormLimits(
            MultipartBodyLengthLimit = long.MaxValue)]
        public async Task<IActionResult>
            RestoreDatabaseBackup(
                IFormFile backupFile)
        {
            var block = RequireAdmin();

            if (block != null)
                return block;

            if (backupFile == null ||
                backupFile.Length == 0)
            {
                TempData["Message"] =
                    "請選擇完整系統備份 ZIP。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "BackupManagement"
                );
            }

            if (!Path.GetExtension(
                    backupFile.FileName)
                .Equals(
                    ".zip",
                    StringComparison.OrdinalIgnoreCase))
            {
                TempData["Message"] =
                    "只允許 ZIP 完整備份檔。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "BackupManagement"
                );
            }


            string uploadedZip = "";
            string rollbackZip = "";

            bool restoreStarted = false;

            try
            {
                string restoreTemp =
                    GetRestoreTempDirectory();

                uploadedZip =
                    Path.Combine(
                        restoreTemp,
                        "restore_" +
                        Guid.NewGuid()
                            .ToString("N") +
                        ".zip"
                    );


                // =========================
                // 1. 上傳直接寫入硬碟
                // =========================

                await using (
                    var destination =
                        new FileStream(
                            uploadedZip,
                            FileMode.Create,
                            FileAccess.Write,
                            FileShare.None,
                            1024 * 1024,
                            FileOptions.Asynchronous |
                            FileOptions.SequentialScan
                        )
                )
                {
                    await backupFile.CopyToAsync(
                        destination
                    );
                }


                // =========================
                // 2. 完整驗證
                // 尚未修改目前系統
                // =========================

                await ValidateSystemBackupZip(
                    uploadedZip
                );


                // =========================
                // 3. 建立硬碟 Rollback ZIP
                // =========================

                rollbackZip =
                    Path.Combine(
                        restoreTemp,
                        "rollback_" +
                        Guid.NewGuid()
                            .ToString("N") +
                        ".zip"
                    );

                await CreateSystemBackupZip(
                    rollbackZip,
                    "SYSTEM_ROLLBACK",
                    "System"
                );


                // 從這裡開始才會修改資料
                restoreStarted = true;


                // =========================
                // 4. 執行 Restore
                // =========================

                await ApplySystemBackupZip(
                    uploadedZip
                );


                TempData["Message"] =
                    "完整系統資料恢復完成。";

                TempData["MessageType"] =
                    "success";

                return RedirectToAction(
                    "BackupManagement"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "完整系統資料恢復失敗"
                );


                // =========================
                // 已開始修改資料
                // → 自動 Rollback
                // =========================

                if (restoreStarted &&
                    !string.IsNullOrWhiteSpace(
                        rollbackZip)
                    &&
                    System.IO.File.Exists(
                        rollbackZip))
                {
                    try
                    {
                        await ApplySystemBackupZip(
                            rollbackZip
                        );

                        _logger.LogWarning(
                            "Restore 失敗，但已成功自動 Rollback。"
                        );

                        TempData["Message"] =
                            "恢復失敗：" +
                            ex.Message +
                            "；系統已自動還原至恢復前狀態。";

                        TempData["MessageType"] =
                            "error";
                    }
                    catch (
                        Exception rollbackEx)
                    {
                        _logger.LogCritical(
                            rollbackEx,
                            "Restore 與 Rollback 均失敗"
                        );

                        TempData["Message"] =
                            "恢復失敗：" +
                            ex.Message +
                            "；自動還原也失敗。" +
                            "請勿繼續修改資料，並使用已下載的完整備份進行人工恢復。";

                        TempData["MessageType"] =
                            "error";
                    }
                }
                else
                {
                    // ZIP 驗證階段就失敗，
                    // 此時完全沒有修改原始資料。
                    TempData["Message"] =
                        "恢復失敗：" +
                        ex.Message;

                    TempData["MessageType"] =
                        "error";
                }

                return RedirectToAction(
                    "BackupManagement"
                );
            }
            finally
            {
                // =========================
                // 5. 一律清除硬碟暫存檔
                // =========================

                SafeDeleteFile(
                    uploadedZip
                );

                SafeDeleteFile(
                    rollbackZip
                );
            }
        }
        [HttpGet]
        public async Task<IActionResult> SurgicalTableManagement()
        {
            var block = RequireLogin();

            if (block != null)
                return block;

            var tablesTask =
                GetSurgicalTables();

            var cadaversTask =
                GetAllData();

            var schedulesTask =
                GetClassSchedules();

            await Task.WhenAll(
                tablesTask,
                cadaversTask,
                schedulesTask
            );

            var tables =
                await tablesTask;

            var cadavers =
                await cadaversTask;

            var schedules =
                await schedulesTask;

            ViewBag.Cadavers =
                cadavers
                    .Where(x => !x.IsDissected)
                    .ToList();

            ViewBag.ClassSchedules =
                schedules;

            return View(tables);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSurgicalTable(
    string tableNumber,
    string tableName)
        {
            var block = RequireTeacher();

            if (block != null)
                return block;

            tableNumber = tableNumber?.Trim() ?? "";
            tableName = tableName?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(tableNumber))
            {
                TempData["Message"] =
                    "新增手術台失敗：請輸入手術台編號。";

                TempData["MessageType"] = "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }
            var existingTables =
    await GetSurgicalTables();

            bool tableNumberExists =
                existingTables.Any(x =>
                    string.Equals(
                        x.TableNumber,
                        tableNumber,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (tableNumberExists)
            {
                TempData["Message"] =
                    $"新增手術台失敗：手術台編號 {tableNumber} 已存在。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }
            if (string.IsNullOrWhiteSpace(tableName))
            {
                tableName =
                    $"手術台 {tableNumber}";
            }

            string rowKey =
                "TABLE_" +
                Guid.NewGuid()
                    .ToString("N");

            await PutSurgicalTableRow(
                rowKey,
                tableNumber,
                tableName,
                true
            );

            TempData["Message"] =
                $"手術台 {tableNumber} 新增成功。";

            TempData["MessageType"] =
                "success";

            return RedirectToAction(
                "SurgicalTableManagement"
            );
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleSurgicalTable(
    string rowKey)
        {
            var block = RequireTeacher();

            if (block != null)
                return block;

            rowKey = rowKey?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(rowKey))
            {
                TempData["Message"] =
                    "操作失敗：缺少手術台 RowKey。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }
            var tables =
                await GetSurgicalTables();

            var table =
                tables.FirstOrDefault(x =>
                    x.RowKey == rowKey
                );

            if (table == null)
            {
                TempData["Message"] =
                    "操作失敗：找不到此手術台。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }
            if (table.IsActive)
            {
                var schedules =
                    await GetClassSchedules();

                bool hasUpcomingSchedule =
                    schedules.Any(x =>
                        x.TableId == table.RowKey
                        &&
                        DateTime.TryParse(
                            x.ClassDate,
                            out DateTime scheduleDate
                        )
                        &&
                        scheduleDate.Date >= DateTime.Today
                    );
                if (hasUpcomingSchedule)
                {
                    TempData["Message"] =
    $"無法停用手術台 {table.TableNumber}：目前仍有尚未結束的上課安排。";

                    TempData["MessageType"] =
                        "error";

                    return RedirectToAction(
                        "SurgicalTableManagement"
                    );
                }
            }
            bool newStatus =
                !table.IsActive;

            await PutSurgicalTableRow(
                table.RowKey,
                table.TableNumber,
                table.TableName,
                newStatus
            );

            TempData["Message"] =
                newStatus
                    ? $"手術台 {table.TableNumber} 已啟用。"
                    : $"手術台 {table.TableNumber} 已停用。";

            TempData["MessageType"] =
                "success";

            return RedirectToAction(
                "SurgicalTableManagement"
            );
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveClassSchedule(
    string classDate,
    string tableId,
    string cadaverRowKey)
        {
            var block = RequireTeacher();

            if (block != null)
                return block;

            classDate =
                classDate?.Trim() ?? "";

            tableId =
                tableId?.Trim() ?? "";

            cadaverRowKey =
                cadaverRowKey?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(classDate) ||
                string.IsNullOrWhiteSpace(tableId) ||
                string.IsNullOrWhiteSpace(cadaverRowKey))
            {
                TempData["Message"] =
                    "新增上課安排失敗：請完整選擇上課日期、手術台與大體老師。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }
            if (!DateTime.TryParse(
        classDate,
        out DateTime parsedClassDate))
            {
                TempData["Message"] =
                    "新增上課安排失敗：上課日期格式錯誤。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }

            parsedClassDate =
                parsedClassDate.Date;

            if (parsedClassDate < DateTime.Today)
            {
                TempData["Message"] =
                    "新增上課安排失敗：不可安排過去日期。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }

            classDate =
                parsedClassDate.ToString("yyyy-MM-dd");
            var tables =
                await GetSurgicalTables();

            var table =
                tables.FirstOrDefault(x =>
                    x.RowKey == tableId
                );

            if (table == null)
            {
                TempData["Message"] =
                    "新增上課安排失敗：找不到此手術台。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }

            if (!table.IsActive)
            {
                TempData["Message"] =
                    "新增上課安排失敗：此手術台目前已停用。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }

            var cadavers =
                await GetAllData();

            var cadaver =
                cadavers.FirstOrDefault(x =>
                    x.RowKey == cadaverRowKey
                );

            if (cadaver == null)
            {
                TempData["Message"] =
                    "新增上課安排失敗：找不到此大體老師。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }
            if (cadaver.IsDissected)
            {
                TempData["Message"] =
                    $"新增上課安排失敗：大體老師 {cadaver.RowKey} 已解剖，無法再安排上課。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }
            var schedules =
    await GetClassSchedules();

            bool tableAlreadyUsed =
                schedules.Any(x =>
                    x.ClassDate == classDate &&
                    x.TableId == tableId
                );

            if (tableAlreadyUsed)
            {
                TempData["Message"] =
                    $"新增上課安排失敗：{classDate} 的手術台 {table.TableNumber} 已經有安排。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }
            bool cadaverAlreadyUsed =
    schedules.Any(x =>
        x.ClassDate == classDate &&
        x.CadaverRowKey == cadaverRowKey
    );

            if (cadaverAlreadyUsed)
            {
                TempData["Message"] =
                    $"新增上課安排失敗：{classDate} 的大體老師 {cadaver.RowKey} 已經有安排。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }
            string classSessionId =
                "CLASS_" +
                classDate.Replace("-", "") +
                "_" +
                Guid.NewGuid()
                    .ToString("N")
                    .Substring(0, 8);

            string rowKey =
                "SCHEDULE_" +
                Guid.NewGuid()
                    .ToString("N");

            await PutClassScheduleRow(
                rowKey,
                classSessionId,
                classDate,
                tableId,
                cadaverRowKey
            );

            TempData["Message"] =
                $"上課安排新增成功：{classDate}，手術台 {table.TableNumber}，大體老師 {cadaver.RowKey}。";

            TempData["MessageType"] =
                "success";

            return RedirectToAction(
                "SurgicalTableManagement"
            );
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveTeachingApplication(
    string applicant,
    string groupName,
    string classTime,
    string age,
    string medicalHistory,
    string diagnosis,
    string department,
    string assistant,
    string mainDoctor,
    string nurse,
    string courseContent,
    string surgeryTopic,
    string procedure,
    string bodyPart)
        {
            var block = RequireLogin();

            if (block != null)
                return block;

            string role =
                HttpContext.Session.GetString("Role")
                ?? "";

            // Teacher 與 Admin 可以進行刪除
            if (role != "Teacher" &&
                role != "Admin")
            {
                return Forbid();
            }

            string loginId =
                HttpContext.Session.GetString("LoginId")
                ?? "";

            applicant =
                applicant?.Trim() ?? "";

            groupName =
                groupName?.Trim() ?? "";

            classTime =
                classTime?.Trim() ?? "";

            age =
                age?.Trim() ?? "";

            medicalHistory =
                medicalHistory?.Trim() ?? "";

            diagnosis =
                diagnosis?.Trim() ?? "";

            department =
                department?.Trim() ?? "";

            assistant =
                assistant?.Trim() ?? "";

            mainDoctor =
                mainDoctor?.Trim() ?? "";

            nurse =
                nurse?.Trim() ?? "";

            courseContent =
                courseContent?.Trim() ?? "";

            surgeryTopic =
                surgeryTopic?.Trim() ?? "";

            procedure =
                procedure?.Trim() ?? "";

            bodyPart =
                bodyPart?.Trim() ?? "";

            // 申請人一律以登入帳號為準
            applicant = loginId;

            if (string.IsNullOrWhiteSpace(groupName) ||
                string.IsNullOrWhiteSpace(classTime))
            {
                TempData["Message"] =
                    "申請失敗：組別與預計時間為必填。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "TeachingApplication"
                );
            }

            if (!DateTime.TryParse(
                    classTime,
                    out DateTime parsedClassTime))
            {
                TempData["Message"] =
                    "申請失敗：時間格式錯誤。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "TeachingApplication"
                );
            }

            string rowKey =
                "APP_" +
                DateTime.Now.ToString(
                    "yyyyMMddHHmmss"
                ) +
                "_" +
                Guid.NewGuid()
                    .ToString("N")
                    .Substring(0, 6);

            string createdAt =
                DateTime.Now.ToString(
                    "yyyy-MM-dd HH:mm:ss"
                );

            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(rowKey)}"">

    <Cell column=""{ToBase64("info:applicant")}"">
      {ToBase64(applicant)}
    </Cell>

    <Cell column=""{ToBase64("info:group_name")}"">
      {ToBase64(groupName)}
    </Cell>

    <Cell column=""{ToBase64("info:class_time")}"">
      {ToBase64(
                  parsedClassTime.ToString(
                      "yyyy-MM-dd HH:mm"
                  )
              )}
    </Cell>

    <Cell column=""{ToBase64("info:age")}"">
      {ToBase64(age)}
    </Cell>

    <Cell column=""{ToBase64("info:medical_history")}"">
      {ToBase64(medicalHistory)}
    </Cell>

    <Cell column=""{ToBase64("info:diagnosis")}"">
      {ToBase64(diagnosis)}
    </Cell>

    <Cell column=""{ToBase64("info:department")}"">
      {ToBase64(department)}
    </Cell>

    <Cell column=""{ToBase64("info:assistant")}"">
      {ToBase64(assistant)}
    </Cell>

    <Cell column=""{ToBase64("info:main_doctor")}"">
      {ToBase64(mainDoctor)}
    </Cell>

    <Cell column=""{ToBase64("info:nurse")}"">
      {ToBase64(nurse)}
    </Cell>

    <Cell column=""{ToBase64("info:course_content")}"">
      {ToBase64(courseContent)}
    </Cell>

    <Cell column=""{ToBase64("info:surgery_topic")}"">
      {ToBase64(surgeryTopic)}
    </Cell>

    <Cell column=""{ToBase64("info:procedure")}"">
      {ToBase64(procedure)}
    </Cell>

    <Cell column=""{ToBase64("info:body_part")}"">
      {ToBase64(bodyPart)}
    </Cell>

    <Cell column=""{ToBase64("info:created_at")}"">
      {ToBase64(createdAt)}
    </Cell>

    <Cell column=""{ToBase64("info:status")}"">
      {ToBase64("Pending")}
    </Cell>

  </Row>
</CellSet>";

            using var content =
                new StringContent(
                    xml,
                    Encoding.UTF8,
                    "text/xml"
                );

            HttpResponseMessage response =
                await _client.PutAsync(
                    $"teaching_application/{rowKey}",
                    content
                );

            if (!response.IsSuccessStatusCode)
            {
                TempData["Message"] =
                    "申請送出失敗，請確認 HBase teaching_application 資料表。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "TeachingApplication"
                );
            }

            TempData["Message"] =
                "申請已成功送出。";

            TempData["MessageType"] =
                "success";

            return RedirectToAction(
                "TeachingApplicationReport"
            );
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteClassSchedule(
            string rowKey)
        {
            var block =
                RequireTeacher();

            if (block != null)
                return block;

            rowKey =
                rowKey?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(rowKey))
            {
                TempData["Message"] =
                    "刪除上課安排失敗：缺少 RowKey。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }

            var schedules =
                await GetClassSchedules();

            var schedule =
                schedules.FirstOrDefault(x =>
                    x.RowKey == rowKey
                );

            if (schedule == null)
            {
                TempData["Message"] =
                    "刪除上課安排失敗：找不到此上課安排。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }

            var response =
                    await _client.DeleteAsync(
                        $"class_schedule/{rowKey}"
                    );

            if (!response.IsSuccessStatusCode)
            {
                TempData["Message"] =
                    "刪除上課安排失敗。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "SurgicalTableManagement"
                );
            }

            TempData["Message"] =
                "上課安排已刪除。";

            TempData["MessageType"] =
                "success";

            return RedirectToAction(
                "SurgicalTableManagement"
            );
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportRecognizedPaperMedicalRecord(
    [FromBody] PaperMedicalRecordImportRequest request)
        {
            if (!IsLogin())
            {
                return Unauthorized(new
                {
                    error = "登入狀態已失效，請重新登入。"
                });
            }

            if (!IsTeacher())
            {
                return StatusCode(403, new
                {
                    error = "只有老師或管理員可以匯入紙本病歷。"
                });
            }

            if (request == null)
            {
                return BadRequest(new
                {
                    error = "缺少紙本病歷辨識結果。"
                });
            }

            request.Row =
                request.Row?.Trim() ?? "";

            request.MedicalRecordNumber =
                request.MedicalRecordNumber?.Trim() ?? "";

            request.PatientName =
                request.PatientName?.Trim() ?? "";

            request.SourceFileName =
                request.SourceFileName?.Trim() ?? "";

            request.FullRawText =
                request.FullRawText?.Trim() ?? "";

            request.Records ??=
                new List<PaperMedicalRecordEntry>();

            request.GlobalWarnings ??=
                new List<string>();

            // 若圖片沒有辨識到病人編號，自動產生 RowKey
            if (string.IsNullOrWhiteSpace(request.Row))
            {
                request.Row =
                    "PAPER_" +
                    DateTime.Now.ToString("yyyyMMddHHmmss");
            }

            // RowKey 僅保留英文字母、數字、底線和連字號
            request.Row = Regex.Replace(
                request.Row,
                @"[^A-Za-z0-9_-]",
                "_"
            );

            if (request.Row.Length > 30)
            {
                request.Row =
                    request.Row[..30];
            }

            if (string.IsNullOrWhiteSpace(request.Row))
            {
                return BadRequest(new
                {
                    error = "無法建立有效的病人編號。"
                });
            }

            try
            {
                List<Cadaver> existingData =
                    await GetAllData();

                bool rowExists =
                    existingData.Any(patient =>
                        string.Equals(
                            patient.RowKey,
                            request.Row,
                            StringComparison.OrdinalIgnoreCase
                        )
                    );

                if (rowExists)
                {
                    return Conflict(new
                    {
                        error =
                            $"病人編號 {request.Row} 已存在，請修改病人編號後再匯入。"
                    });
                }

                if (!string.IsNullOrWhiteSpace(
                        request.MedicalRecordNumber))
                {
                    bool medicalRecordNumberExists =
                        existingData.Any(patient =>
                            string.Equals(
                                patient.MedicalRecordNumber,
                                request.MedicalRecordNumber,
                                StringComparison.OrdinalIgnoreCase
                            )
                        );

                    if (medicalRecordNumberExists)
                    {
                        return Conflict(new
                        {
                            error =
                                $"病歷號碼 {request.MedicalRecordNumber} 已存在。"
                        });
                    }
                }

                // 建立病人基本資料
                await PutPatientRow(
                    request.Row,
                    request.MedicalRecordNumber,
                    "不詳",
                    "不詳",
                    "病人不知",
                    "待補",
                    "待補",
                    "0",
                    "paper-ai",
                    DateTime.Now.ToString("yyyyMMddHHmmss")
                );

                // 建立病人 FHIR-like 快照
                await SavePatientSnapshot(
            request.Row,
            "",
            "",
            "",
            ""
        );

                int importedRecordCount = 0;

                foreach (PaperMedicalRecordEntry entry
                         in request.Records)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    string recordId =
                        Guid.NewGuid().ToString("N");

                    string occurredAt =
                        BuildPaperRecordOccurredAt(
                            entry.RecordDate,
                            entry.RecordTime
                        );

                    string finding =
                        BuildPaperRecordFinding(entry);

                    string note =
                        BuildPaperRecordNote(
                            entry,
                            request.SourceFileName
                        );

                    string diagnosis =
                        string.IsNullOrWhiteSpace(
                            entry.Assessment)
                            ? "PaperMedicalRecordRecognition"
                            : entry.Assessment.Trim();

                    string treatment =
                        string.IsNullOrWhiteSpace(
                            entry.Treatment)
                            ? entry.NursingNote?.Trim() ?? ""
                            : entry.Treatment.Trim();

                    string physician =
                        HttpContext.Session.GetString(
                            "LoginId")
                        ?? "系統";

                    string fhirJson =
                        BuildObservationFhirJson(
                            request.Row,
                            recordId,
                            occurredAt,
                            diagnosis,
                            "紙本病歷",
                            finding,
                            treatment,
                            physician,
                            note
                        );

                    await PutMedicalRecord(
                        request.Row,
                        new MedicalRecord
                        {
                            Id = recordId,
                            ResourceType = "Observation",
                            OccurredAt = occurredAt,
                            Diagnosis = diagnosis,
                            BodyPart = "紙本病歷",
                            Finding = finding,
                            Treatment = treatment,
                            Physician = physician,
                            Note = note,
                            FhirJson = fhirJson
                        }
                    );

                    importedRecordCount++;
                }

                // 如果 AI 沒有拆出 records，
                // 但有完整 OCR 文字，仍建立一筆病歷紀錄
                if (importedRecordCount == 0 &&
                    !string.IsNullOrWhiteSpace(
                        request.FullRawText))
                {
                    string recordId =
                        Guid.NewGuid().ToString("N");

                    string occurredAt =
                        DateTime.Now.ToString(
                            "yyyy-MM-ddTHH:mm:ss"
                        );

                    string physician =
                        HttpContext.Session.GetString(
                            "LoginId")
                        ?? "系統";

                    string note =
                        "資料來源：AI 紙本病歷辨識";

                    if (!string.IsNullOrWhiteSpace(
                            request.SourceFileName))
                    {
                        note +=
                            Environment.NewLine +
                            $"來源檔案：{request.SourceFileName}";
                    }

                    note +=
                        Environment.NewLine +
                        "AI 未能拆分病歷紀錄，已保留完整辨識文字。";

                    if (request.GlobalWarnings.Count > 0)
                    {
                        string warnings =
                            string.Join(
                                "、",
                                request.GlobalWarnings
                                    .Where(item =>
                                        !string.IsNullOrWhiteSpace(item))
                                    .Select(item =>
                                        item.Trim())
                            );

                        if (!string.IsNullOrWhiteSpace(
                                warnings))
                        {
                            note +=
                                Environment.NewLine +
                                $"辨識警告：{warnings}";
                        }
                    }

                    string fhirJson =
                        BuildObservationFhirJson(
                            request.Row,
                            recordId,
                            occurredAt,
                            "PaperMedicalRecordRecognition",
                            "紙本病歷",
                            request.FullRawText,
                            "",
                            physician,
                            note
                        );

                    await PutMedicalRecord(
                        request.Row,
                        new MedicalRecord
                        {
                            Id = recordId,
                            ResourceType = "Observation",
                            OccurredAt = occurredAt,
                            Diagnosis =
                                "PaperMedicalRecordRecognition",
                            BodyPart = "紙本病歷",
                            Finding = request.FullRawText,
                            Treatment = "",
                            Physician = physician,
                            Note = note,
                            FhirJson = fhirJson
                        }
                    );

                    importedRecordCount = 1;
                }

                // 若連完整文字也沒有，避免建立空病人資料
                if (importedRecordCount == 0)
                {
                    await _client.DeleteAsync(
                        $"cadaver/{request.Row}"
                    );

                    return BadRequest(new
                    {
                        error =
                            "辨識結果沒有可匯入的病歷內容。"
                    });
                }

                return Ok(new
                {
                    success = true,
                    row = request.Row,
                    importedRecordCount,
                    message =
                        $"紙本病歷已匯入，病人編號為 {request.Row}，共建立 {importedRecordCount} 筆病歷紀錄。"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "匯入紙本病歷失敗。Row={Row}",
                    request.Row
                );

                return StatusCode(500, new
                {
                    error = "紙本病歷匯入失敗。",
                    detail =
                        ex.InnerException?.Message
                        ?? ex.Message
                });
            }
        }
        private static string BuildPaperRecordOccurredAt(
            string? recordDate,
            string? recordTime)
        {
            string date =
                recordDate?.Trim() ?? "";

            string time =
                recordTime?.Trim() ?? "";

            // 沒有辨識到日期時，使用匯入當下時間
            if (string.IsNullOrWhiteSpace(date))
            {
                return DateTime.Now.ToString(
                    "yyyy-MM-ddTHH:mm:ss"
                );
            }

            string combined =
                string.IsNullOrWhiteSpace(time)
                    ? date
                    : $"{date} {time}";

            // 西元日期可以正常解析時，轉成統一格式
            if (DateTime.TryParse(
                    combined,
                    out DateTime parsedDate))
            {
                return parsedDate.ToString(
                    "yyyy-MM-ddTHH:mm:ss"
                );
            }

            // 民國日期、簡寫日期或特殊格式無法解析時，
            // 直接保留 AI 辨識出的原始日期與時間
            return combined;
        }

        private static string BuildPaperRecordFinding(
            PaperMedicalRecordEntry entry)
        {
            var parts = new List<string>();

            void AddPart(
                string label,
                string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    parts.Add(
                        $"{label}：{value.Trim()}"
                    );
                }
            }

            AddPart(
                "症狀",
                entry.Symptoms
            );

            AddPart(
                "評估",
                entry.Assessment
            );

            AddPart(
                "護理紀錄",
                entry.NursingNote
            );

            AddPart(
                "血壓",
                entry.BloodPressure
            );

            AddPart(
                "脈搏",
                entry.Pulse
            );

            AddPart(
                "呼吸速率",
                entry.RespiratoryRate
            );

            AddPart(
                "體溫",
                entry.Temperature
            );

            AddPart(
                "血氧",
                entry.OxygenSaturation
            );

            AddPart(
                "體重",
                entry.BodyWeight
            );

            if (entry.Medications != null &&
                entry.Medications.Count > 0)
            {
                string medications =
                    string.Join(
                        "、",
                        entry.Medications
                            .Where(item =>
                                !string.IsNullOrWhiteSpace(item))
                            .Select(item =>
                                item.Trim())
                    );

                if (!string.IsNullOrWhiteSpace(
                        medications))
                {
                    parts.Add(
                        $"藥物：{medications}"
                    );
                }
            }

            if (entry.LabFindings != null &&
                entry.LabFindings.Count > 0)
            {
                string labFindings =
                    string.Join(
                        "、",
                        entry.LabFindings
                            .Where(item =>
                                !string.IsNullOrWhiteSpace(item))
                            .Select(item =>
                                item.Trim())
                    );

                if (!string.IsNullOrWhiteSpace(
                        labFindings))
                {
                    parts.Add(
                        $"檢驗發現：{labFindings}"
                    );
                }
            }

            // 若所有結構化欄位都沒有內容，
            // 至少保留 AI 辨識的原始文字
            if (parts.Count == 0 &&
                !string.IsNullOrWhiteSpace(
                    entry.RawText))
            {
                parts.Add(
                    entry.RawText.Trim()
                );
            }

            return string.Join(
                Environment.NewLine,
                parts
            );
        }

        private static string BuildPaperRecordNote(
            PaperMedicalRecordEntry entry,
            string sourceFileName)
        {
            var parts = new List<string>
    {
        "資料來源：AI 紙本病歷辨識"
    };

            if (!string.IsNullOrWhiteSpace(
                    sourceFileName))
            {
                parts.Add(
                    $"來源檔案：{sourceFileName.Trim()}"
                );
            }

            double confidence =
                entry.Confidence;

            // 避免模型回傳超出 0～1 的數值
            if (confidence < 0)
            {
                confidence = 0;
            }
            else if (confidence > 1)
            {
                confidence = 1;
            }

            parts.Add(
                $"辨識可信度：{confidence:P0}"
            );

            if (entry.Warnings != null &&
                entry.Warnings.Count > 0)
            {
                string warnings =
                    string.Join(
                        "、",
                        entry.Warnings
                            .Where(item =>
                                !string.IsNullOrWhiteSpace(item))
                            .Select(item =>
                                item.Trim())
                    );

                if (!string.IsNullOrWhiteSpace(
                        warnings))
                {
                    parts.Add(
                        $"辨識警告：{warnings}"
                    );
                }
            }

            if (!string.IsNullOrWhiteSpace(
                    entry.RawText))
            {
                parts.Add(
                    "原始辨識文字：" +
                    entry.RawText.Trim()

                );
            }

            return string.Join(
                Environment.NewLine,
                parts
            );
        }


        private async Task<List<QuizQuestion>> GetQuizQuestions()
        {
            var result = new List<QuizQuestion>();

            try
            {
                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(10)
                    );

                var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        "quiz_question/*"
                    );

                request.Headers.Add(
                    "Accept",
                    "application/json"
                );

                var response =
                    await _client.SendAsync(
                        request,
                        cts.Token
                    );

                if (!response.IsSuccessStatusCode)
                    return result;

                string json =
                    await response.Content
                        .ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(json))
                    return result;

                using JsonDocument doc =
                    JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty(
                        "Row",
                        out JsonElement rows))
                {
                    return result;
                }

                foreach (var row in rows.EnumerateArray())
                {
                    string id =
                        Decode(
                            row.GetProperty("key")
                                .GetString()
                            ?? ""
                        );

                    var question =
                        new QuizQuestion
                        {
                            Id = id
                        };

                    if (!row.TryGetProperty(
                            "Cell",
                            out JsonElement cells))
                    {
                        continue;
                    }

                    foreach (var cell in cells.EnumerateArray())
                    {
                        string column =
                            Decode(
                                cell.GetProperty("column")
                                    .GetString()
                                ?? ""
                            );

                        string value =
                            Decode(
                                cell.GetProperty("$")
                                    .GetString()
                                ?? ""
                            );

                        switch (column)
                        {
                            case "q:question":
                                question.QuestionText = value;
                                break;
                            case "q:option_a":
                                question.OptionA = value;
                                break;
                            case "q:option_b":
                                question.OptionB = value;
                                break;
                            case "q:option_c":
                                question.OptionC = value;
                                break;
                            case "q:option_d":
                                question.OptionD = value;
                                break;
                            case "q:correct_answer":
                                question.CorrectAnswer =
                                    value.Trim()
                                         .ToUpperInvariant();
                                break;
                            case "q:explanation":
                                question.Explanation = value;
                                break;
                            case "q:is_active":
                                question.IsActive =
                                    value == "1" ||
                                    value.Equals(
                                        "true",
                                        StringComparison.OrdinalIgnoreCase
                                    );
                                break;
                            case "q:created_by":
                                question.CreatedBy = value;
                                break;
                            case "q:created_at":
                                question.CreatedAt = value;
                                break;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(
                            question.QuestionText))
                    {
                        result.Add(question);
                    }
                }
            }
            catch
            {
                // HBase table 尚未建立、REST timeout 或資料解析錯誤時，
                // 回傳空題庫，避免整個 MVC 頁面失敗。
            }

            return result;
        }


        private async Task PutQuizQuestion(
            QuizQuestion question)
        {
            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(question.Id)}"">
    <Cell column=""{ToBase64("q:question")}"">{ToBase64(question.QuestionText)}</Cell>
    <Cell column=""{ToBase64("q:option_a")}"">{ToBase64(question.OptionA)}</Cell>
    <Cell column=""{ToBase64("q:option_b")}"">{ToBase64(question.OptionB)}</Cell>
    <Cell column=""{ToBase64("q:option_c")}"">{ToBase64(question.OptionC)}</Cell>
    <Cell column=""{ToBase64("q:option_d")}"">{ToBase64(question.OptionD)}</Cell>
    <Cell column=""{ToBase64("q:correct_answer")}"">{ToBase64(question.CorrectAnswer)}</Cell>
    <Cell column=""{ToBase64("q:explanation")}"">{ToBase64(question.Explanation)}</Cell>
    <Cell column=""{ToBase64("q:is_active")}"">{ToBase64(question.IsActive ? "1" : "0")}</Cell>
    <Cell column=""{ToBase64("q:created_by")}"">{ToBase64(question.CreatedBy)}</Cell>
    <Cell column=""{ToBase64("q:created_at")}"">{ToBase64(question.CreatedAt)}</Cell>
  </Row>
</CellSet>";

            var content =
                new StringContent(
                    xml,
                    Encoding.UTF8,
                    "text/xml"
                );

            var response =
                await _client.PutAsync(
                    $"quiz_question/{question.Id}",
                    content
                );

            response.EnsureSuccessStatusCode();
        }


        private async Task SetQuizQuestionActive(
            string id,
            bool isActive)
        {
            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(id)}"">
    <Cell column=""{ToBase64("q:is_active")}"">{ToBase64(isActive ? "1" : "0")}</Cell>
  </Row>
</CellSet>";

            var content =
                new StringContent(
                    xml,
                    Encoding.UTF8,
                    "text/xml"
                );

            var response =
                await _client.PutAsync(
                    $"quiz_question/{id}",
                    content
                );

            response.EnsureSuccessStatusCode();
        }


        private static string BuildQuizVersion(
            IEnumerable<QuizQuestion> questions)
        {
            string raw =
                string.Join(
                    "|",
                    questions
                        .Select(x => x.Id)
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x))
                        .OrderBy(x => x)
                );

            byte[] hash =
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(raw)
                );

            return Convert.ToHexString(hash);
        }
        private static string BuildQuizVersionFromSnapshots(
    IEnumerable<QuizQuestionSnapshot> questions)
        {
            string raw =
                string.Join(
                    "|",
                    questions
                        .Select(x => x.Id)
                        .Where(x =>
                            !string.IsNullOrWhiteSpace(x))
                        .OrderBy(x => x)
                );

            if (string.IsNullOrWhiteSpace(raw))
                return "";

            byte[] hash =
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(raw)
                );

            return Convert.ToHexString(hash);
        }

        private static QuizQuestionSnapshot ToQuizSnapshot(
            QuizQuestion question)
        {
            return new QuizQuestionSnapshot
            {
                Id = question.Id,
                QuestionText = question.QuestionText,
                OptionA = question.OptionA,
                OptionB = question.OptionB,
                OptionC = question.OptionC,
                OptionD = question.OptionD,
                CorrectAnswer = question.CorrectAnswer,
                Explanation = question.Explanation
            };
        }


        private async Task SaveQuizAttempt(
            string studentId,
            int score,
            int total,
            Dictionary<string, string> answers,
            List<QuizQuestion> questions)
        {
            string attemptId =
                Guid.NewGuid()
                    .ToString("N");

            string submittedAt =
                DateTime.Now.ToString(
                    "yyyy-MM-ddTHH:mm:ss"
                );

            string quizVersion =
                BuildQuizVersion(questions);

            string answersJson =
                JsonSerializer.Serialize(
                    answers
                );

            string questionsJson =
                JsonSerializer.Serialize(
                    questions
                        .Select(ToQuizSnapshot)
                        .ToList()
                );

            string xml = $@"
<CellSet>
  <Row key=""{ToBase64(attemptId)}"">
    <Cell column=""{ToBase64("a:student_id")}"">{ToBase64(studentId)}</Cell>
    <Cell column=""{ToBase64("a:score")}"">{ToBase64(score.ToString())}</Cell>
    <Cell column=""{ToBase64("a:total")}"">{ToBase64(total.ToString())}</Cell>
    <Cell column=""{ToBase64("a:answers_json")}"">{ToBase64(answersJson)}</Cell>
    <Cell column=""{ToBase64("a:questions_json")}"">{ToBase64(questionsJson)}</Cell>
    <Cell column=""{ToBase64("a:quiz_version")}"">{ToBase64(quizVersion)}</Cell>
    <Cell column=""{ToBase64("a:submitted_at")}"">{ToBase64(submittedAt)}</Cell>
  </Row>
</CellSet>";

            var content =
                new StringContent(
                    xml,
                    Encoding.UTF8,
                    "text/xml"
                );

            var response =
                await _client.PutAsync(
                    $"quiz_attempt/{attemptId}",
                    content
                );

            response.EnsureSuccessStatusCode();
        }


        private async Task<List<QuizAttemptRecord>> GetQuizAttempts()
        {
            var result =
                new List<QuizAttemptRecord>();

            try
            {
                using var cts =
                    new CancellationTokenSource(
                        TimeSpan.FromSeconds(10)
                    );

                var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        "quiz_attempt/*"
                    );

                request.Headers.Add(
                    "Accept",
                    "application/json"
                );

                var response =
                    await _client.SendAsync(
                        request,
                        cts.Token
                    );

                if (!response.IsSuccessStatusCode)
                    return result;

                string json =
                    await response.Content
                        .ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(json))
                    return result;

                using JsonDocument doc =
                    JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty(
                        "Row",
                        out JsonElement rows))
                {
                    return result;
                }

                foreach (var row in rows.EnumerateArray())
                {
                    var attempt =
                        new QuizAttemptRecord
                        {
                            AttemptId =
                                Decode(
                                    row.GetProperty("key")
                                        .GetString()
                                    ?? ""
                                )
                        };

                    if (!row.TryGetProperty(
                            "Cell",
                            out JsonElement cells))
                    {
                        continue;
                    }

                    foreach (var cell in cells.EnumerateArray())
                    {
                        string column =
                            Decode(
                                cell.GetProperty("column")
                                    .GetString()
                                ?? ""
                            );

                        string value =
                            Decode(
                                cell.GetProperty("$")
                                    .GetString()
                                ?? ""
                            );

                        switch (column)
                        {
                            case "a:student_id":
                                attempt.StudentId = value;
                                break;

                            case "a:score":
                                int.TryParse(
                                    value,
                                    out int score
                                );
                                attempt.Score = score;
                                break;

                            case "a:total":
                                int.TryParse(
                                    value,
                                    out int total
                                );
                                attempt.Total = total;
                                break;

                            case "a:answers_json":
                                try
                                {
                                    attempt.Answers =
                                        JsonSerializer.Deserialize<
                                            Dictionary<string, string>
                                        >(value)
                                        ?? new Dictionary<string, string>();
                                }
                                catch
                                {
                                    attempt.Answers =
                                        new Dictionary<string, string>();
                                }
                                break;

                            case "a:questions_json":
                                try
                                {
                                    attempt.Questions =
                                        JsonSerializer.Deserialize<
                                            List<QuizQuestionSnapshot>
                                        >(value)
                                        ?? new List<QuizQuestionSnapshot>();
                                }
                                catch
                                {
                                    attempt.Questions =
                                        new List<QuizQuestionSnapshot>();
                                }
                                break;

                            case "a:quiz_version":
                                attempt.QuizVersion = value;
                                break;

                            case "a:submitted_at":
                                attempt.SubmittedAt = value;
                                break;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(
                            attempt.StudentId))
                    {
                        result.Add(attempt);
                    }
                }
            }
            catch
            {
                // 沒有作答紀錄時回傳空集合。
            }

            return result;
        }


        private static bool AttemptMatchesCurrentQuiz(
            QuizAttemptRecord attempt,
            List<QuizQuestion> activeQuestions,
            string currentQuizVersion)
        {
            if (!string.IsNullOrWhiteSpace(
                    attempt.QuizVersion))
            {
                return string.Equals(
                    attempt.QuizVersion,
                    currentQuizVersion,
                    StringComparison.OrdinalIgnoreCase
                );
            }

            // 舊版紀錄沒有 quiz_version 時，用作答題目 ID 判斷。
            var currentIds =
                activeQuestions
                    .Select(x => x.Id)
                    .OrderBy(x => x)
                    .ToList();

            var attemptIds =
                attempt.Answers.Keys
                    .OrderBy(x => x)
                    .ToList();

            return currentIds.SequenceEqual(
                attemptIds,
                StringComparer.OrdinalIgnoreCase
            );
        }


        private static List<QuizQuestionSnapshot>
            BuildAttemptQuestionFallback(
                QuizAttemptRecord attempt,
                List<QuizQuestion> allQuestions)
        {
            if (attempt.Questions.Count > 0)
                return attempt.Questions;

            // 舊作答紀錄沒有 questions_json：
            // 若題目仍在題庫，就用現有題目補顯示。
            return allQuestions
                .Where(x =>
                    attempt.Answers.ContainsKey(x.Id))
                .Select(ToQuizSnapshot)
                .ToList();
        }


        // ============================================================
        // 學生：作答頁
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> Quiz()
        {
            var block = RequireLogin();
            if (block != null)
                return block;

            string role =
                HttpContext.Session.GetString("Role") ?? "";

            string studentId =
                HttpContext.Session.GetString("LoginId") ?? "";

            if (role != "Student")
            {
                TempData["Message"] = "只有學生帳號可以進行測驗。";
                return RedirectToAction("Index");
            }

            var questions =
                (await GetQuizQuestions())
                    .Where(x => x.IsActive)
                    .OrderBy(x =>
                        DateTime.TryParse(x.CreatedAt, out DateTime d)
                            ? d
                            : DateTime.MinValue)
                    .ToList();

            ViewBag.Submitted = false;
            ViewBag.AlreadyCompleted = false;

            if (questions.Count > 0)
            {
                string currentQuizVersion =
                    BuildQuizVersion(questions);

                var attempts =
                    await GetQuizAttempts();

                var completedAttempt =
                    attempts
                        .Where(x =>
                            string.Equals(
                                x.StudentId,
                                studentId,
                                StringComparison.OrdinalIgnoreCase)
                            &&
                            AttemptMatchesCurrentQuiz(
                                x,
                                questions,
                                currentQuizVersion))
                        .OrderByDescending(x =>
                            DateTime.TryParse(
                                x.SubmittedAt,
                                out DateTime d)
                                ? d
                                : DateTime.MinValue)
                        .FirstOrDefault();

                if (completedAttempt != null)
                {
                    ViewBag.AlreadyCompleted = true;
                    ViewBag.CompletedScore = completedAttempt.Score;
                    ViewBag.CompletedTotal = completedAttempt.Total;
                    ViewBag.CompletedAt = completedAttempt.SubmittedAt;
                    ViewBag.CompletedAttemptId = completedAttempt.AttemptId;
                }
            }

            return View(questions);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitQuiz()
        {
            var block =
                RequireLogin();

            if (block != null)
                return block;

            string role =
                HttpContext.Session.GetString("Role")
                ?? "";

            string studentId =
                HttpContext.Session.GetString("LoginId")
                ?? "";

            if (role != "Student")
            {
                TempData["Message"] =
                    "只有學生帳號可以提交測驗。";

                return RedirectToAction("Index");
            }

            var questions =
                (await GetQuizQuestions())
                    .Where(x => x.IsActive)
                    .OrderBy(x =>
                        DateTime.TryParse(
                            x.CreatedAt,
                            out DateTime d
                        )
                            ? d
                            : DateTime.MinValue
                    )
                    .ToList();

            if (questions.Count == 0)
            {
                TempData["Message"] =
                    "目前沒有可作答的測驗題目。";

                return RedirectToAction("Quiz");
            }

            string currentQuizVersion =
                BuildQuizVersion(questions);

            var previousAttempts =
                await GetQuizAttempts();

            bool alreadyCompleted =
                previousAttempts.Any(x =>
                    string.Equals(
                        x.StudentId,
                        studentId,
                        StringComparison.OrdinalIgnoreCase)
                    &&
                    AttemptMatchesCurrentQuiz(
                        x,
                        questions,
                        currentQuizVersion));

            if (alreadyCompleted)
            {
                TempData["Message"] =
                    "這份測驗已作答完畢，可至歷史紀錄查看作答內容與正確答案。";

                TempData["MessageType"] = "success";

                return RedirectToAction("Quiz");
            }

            var answers =
                new Dictionary<string, string>();

            int score = 0;

            foreach (var question in questions)
            {
                string key =
                    $"answer_{question.Id}";

                string answer =
                    Request.Form[key]
                        .ToString()
                        .Trim()
                        .ToUpperInvariant();

                answers[question.Id] =
                    answer;

                if (answer ==
                    question.CorrectAnswer)
                {
                    score++;
                }
            }

            try
            {
                await SaveQuizAttempt(
                    studentId,
                    score,
                    questions.Count,
                    answers,
                    questions
                );
            }
            catch
            {
                ViewBag.SaveWarning =
                    "測驗已完成，但作答紀錄未成功寫入 HBase。請確認 quiz_attempt 資料表是否可正常存取。";
            }

            ViewBag.Submitted = true;
            ViewBag.Score = score;
            ViewBag.Total = questions.Count;
            ViewBag.Answers = answers;

            return View(
                "Quiz",
                questions
            );
        }


        // ============================================================
        // 學生：歷史作答紀錄
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> QuizHistory()
        {
            var block =
                RequireLogin();

            if (block != null)
                return block;

            string role =
                HttpContext.Session.GetString("Role")
                ?? "";

            string studentId =
                HttpContext.Session.GetString("LoginId")
                ?? "";

            if (role != "Student")
            {
                TempData["Message"] =
                    "只有學生帳號可以查看自己的測驗歷史紀錄。";

                return RedirectToAction("Index");
            }

            var attempts =
                (await GetQuizAttempts())
                    .Where(x =>
                        string.Equals(
                            x.StudentId,
                            studentId,
                            StringComparison.OrdinalIgnoreCase
                        ))
                    .OrderByDescending(x =>
                        DateTime.TryParse(
                            x.SubmittedAt,
                            out DateTime d
                        )
                            ? d
                            : DateTime.MinValue
                    )
                    .ToList();

            return View(attempts);
        }


        // ============================================================
        // 學生 / 老師 / 管理員：單次作答明細
        // ============================================================

        [HttpGet]
        public async Task<IActionResult> QuizAttemptDetail(
            string attemptId)
        {
            var block =
                RequireLogin();

            if (block != null)
                return block;

            attemptId =
                attemptId?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(attemptId))
            {
                return RedirectToAction("Index");
            }

            string role =
                HttpContext.Session.GetString("Role")
                ?? "";

            string loginId =
                HttpContext.Session.GetString("LoginId")
                ?? "";

            var attempt =
                (await GetQuizAttempts())
                    .FirstOrDefault(x =>
                        string.Equals(
                            x.AttemptId,
                            attemptId,
                            StringComparison.OrdinalIgnoreCase
                        ));

            if (attempt == null)
            {
                TempData["Message"] =
                    "找不到此測驗作答紀錄。";

                return role == "Student"
                    ? RedirectToAction("QuizHistory")
                    : RedirectToAction("QuizProgress");
            }

            bool canView =
                role == "Teacher"
                || role == "Admin"
                || (
                    role == "Student"
                    && string.Equals(
                        attempt.StudentId,
                        loginId,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (!canView)
            {
                return Forbid();
            }

            var allQuestions =
                await GetQuizQuestions();

            attempt.Questions =
                BuildAttemptQuestionFallback(
                    attempt,
                    allQuestions
                );

            return View(attempt);
        }


        // ============================================================
        // 老師 / 管理員：學生作答狀況
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> QuizProgress(
            string version = "")
        {
            var block =
                RequireTeacher();

            if (block != null)
                return block;

            var allQuestions =
                await GetQuizQuestions();

            var activeQuestions =
                allQuestions
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.CreatedAt)
                    .ToList();

            string currentQuizVersion =
                activeQuestions.Count > 0
                    ? BuildQuizVersion(activeQuestions)
                    : "";

            var attempts =
                await GetQuizAttempts();

            // ==========================================
            // 所有可以查看的版本
            // ==========================================
            var versions =
                new List<QuizProgressVersion>();

            // 目前版本
            if (activeQuestions.Count > 0)
            {
                versions.Add(
                    new QuizProgressVersion
                    {
                        QuizVersion =
                            currentQuizVersion,

                        DisplayName =
                            "目前版本",

                        Questions =
                            activeQuestions
                                .Select(ToQuizSnapshot)
                                .ToList(),

                        IsCurrent = true
                    }
                );
            }

            // ==========================================
            // 歷史版本
            // ==========================================
            var historicalVersions =
                attempts
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(
                            x.QuizVersion))
                    .GroupBy(
                        x => x.QuizVersion,
                        StringComparer.OrdinalIgnoreCase
                    )
                    .Where(group =>
                        !string.Equals(
                            group.Key,
                            currentQuizVersion,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .Select(group =>
                    {
                        var representative =
                            group
                                .OrderByDescending(x =>
                                    DateTime.TryParse(
                                        x.SubmittedAt,
                                        out DateTime d)
                                        ? d
                                        : DateTime.MinValue
                                )
                                .First();

                        var snapshots =
                            BuildAttemptQuestionFallback(
                                representative,
                                allQuestions
                            );

                        string shortVersion =
                            group.Key.Length > 12
                                ? group.Key.Substring(0, 12)
                                : group.Key;

                        return new QuizProgressVersion
                        {
                            QuizVersion =
                                group.Key,

                            DisplayName =
                                $"歷史版本 {shortVersion}",

                            Questions =
                                snapshots
                        };
                    })
                    .Where(x =>
                        x.Questions.Count > 0
                    )
                    .ToList();

            versions.AddRange(
                historicalVersions
            );


            // ==========================================
            // 測試用假版本
            // 測完可直接刪除這一段
            // ==========================================

            versions.Add(
                new QuizProgressVersion
                {
                    QuizVersion =
                        "TEST_VERSION_001",

                    DisplayName =
                        "測試版本 1",

                    IsTest = true,

                    Questions =
                        new List<QuizQuestionSnapshot>
                        {
                    new QuizQuestionSnapshot
                    {
                        Id = "test_v1_q1",
                        QuestionText =
                            "人體最大的器官為何？",
                        OptionA = "心臟",
                        OptionB = "皮膚",
                        OptionC = "肝臟",
                        OptionD = "肺臟",
                        CorrectAnswer = "B",
                        Explanation =
                            "皮膚是人體最大的器官。"
                    },

                    new QuizQuestionSnapshot
                    {
                        Id = "test_v1_q2",
                        QuestionText =
                            "負責過濾血液並產生尿液的器官為何？",
                        OptionA = "腎臟",
                        OptionB = "肺臟",
                        OptionC = "胃",
                        OptionD = "胰臟",
                        CorrectAnswer = "A",
                        Explanation =
                            "腎臟負責過濾血液並形成尿液。"
                    },

                    new QuizQuestionSnapshot
                    {
                        Id = "test_v1_q3",
                        QuestionText =
                            "人體主要負責氣體交換的器官為何？",
                        OptionA = "肝臟",
                        OptionB = "腎臟",
                        OptionC = "肺臟",
                        OptionD = "脾臟",
                        CorrectAnswer = "C",
                        Explanation =
                            "肺臟是主要的氣體交換器官。"
                    }
                        }
                }
            );

            versions.Add(
                new QuizProgressVersion
                {
                    QuizVersion =
                        "TEST_VERSION_002",

                    DisplayName =
                        "測試版本 2",

                    IsTest = true,

                    Questions =
                        new List<QuizQuestionSnapshot>
                        {
                    new QuizQuestionSnapshot
                    {
                        Id = "test_v2_q1",
                        QuestionText =
                            "心臟主要位於人體哪個腔室？",
                        OptionA = "胸腔",
                        OptionB = "腹腔",
                        OptionC = "骨盆腔",
                        OptionD = "顱腔",
                        CorrectAnswer = "A",
                        Explanation =
                            "心臟位於胸腔中的縱膈。"
                    },

                    new QuizQuestionSnapshot
                    {
                        Id = "test_v2_q2",
                        QuestionText =
                            "肝臟主要位於腹部哪一側？",
                        OptionA = "左下",
                        OptionB = "右上",
                        OptionC = "右下",
                        OptionD = "正中央",
                        CorrectAnswer = "B",
                        Explanation =
                            "肝臟大部分位於右上腹。"
                    }
                        }
                }
            );


            // ==========================================
            // 決定目前要看的版本
            // ==========================================

            QuizProgressVersion? selectedVersion =
                null;

            if (!string.IsNullOrWhiteSpace(
                    version))
            {
                selectedVersion =
                    versions.FirstOrDefault(x =>
                        string.Equals(
                            x.QuizVersion,
                            version,
                            StringComparison.OrdinalIgnoreCase
                        ));
            }

            selectedVersion ??=
                versions.FirstOrDefault(x =>
                    x.IsCurrent);

            selectedVersion ??=
                versions.FirstOrDefault();


            var selectedQuestions =
                selectedVersion?.Questions
                ?? new List<QuizQuestionSnapshot>();

            string selectedQuizVersion =
                selectedVersion?.QuizVersion
                ?? "";


            // ==========================================
            // 學生帳號
            // ==========================================

            var students =
                LoadUsers()
                    .Where(x =>
                        x.Role == "Student")
                    .OrderBy(x =>
                        x.LoginId)
                    .ToList();

            var progress =
                new List<QuizStudentProgress>();


            // ==========================================
            // 此版本每位學生的作答
            // ==========================================

            foreach (var student in students)
            {
                QuizAttemptRecord? latestAttempt =
                    null;

                if (!string.IsNullOrWhiteSpace(
                        selectedQuizVersion))
                {
                    latestAttempt =
                        attempts
                            .Where(x =>
                                string.Equals(
                                    x.StudentId,
                                    student.LoginId,
                                    StringComparison.OrdinalIgnoreCase
                                )
                                &&
                                string.Equals(
                                    x.QuizVersion,
                                    selectedQuizVersion,
                                    StringComparison.OrdinalIgnoreCase
                                )
                            )
                            .OrderByDescending(x =>
                                DateTime.TryParse(
                                    x.SubmittedAt,
                                    out DateTime d)
                                    ? d
                                    : DateTime.MinValue
                            )
                            .FirstOrDefault();
                }

                progress.Add(
                    new QuizStudentProgress
                    {
                        StudentId =
                            student.LoginId,

                        HasAnswered =
                            latestAttempt != null,

                        Score =
                            latestAttempt?.Score
                            ?? 0,

                        Total =
                            latestAttempt?.Total
                            ?? selectedQuestions.Count,

                        SubmittedAt =
                            latestAttempt?.SubmittedAt
                            ?? "",

                        AttemptId =
                            latestAttempt?.AttemptId
                            ?? ""
                    }
                );
            }


            ViewBag.ActiveQuestionCount =
                selectedQuestions.Count;

            ViewBag.CurrentQuizVersion =
                currentQuizVersion;

            ViewBag.SelectedQuizVersion =
                selectedQuizVersion;

            ViewBag.SelectedVersionName =
                selectedVersion?.DisplayName
                ?? "";

            ViewBag.QuizProgressVersions =
                versions;

            ViewBag.SelectedQuestions =
                selectedQuestions;

            return View(progress);
        }

        // ============================================================
        // 老師 / 管理員：出題管理
        // ============================================================
        [HttpGet]
        public async Task<IActionResult> QuizManage()
        {
            var block =
                RequireTeacher();

            if (block != null)
                return block;

            var questions =
                (await GetQuizQuestions())
                    .OrderByDescending(x =>
                        DateTime.TryParse(
                            x.CreatedAt,
                            out DateTime d
                        )
                            ? d
                            : DateTime.MinValue
                    )
                    .ToList();


            // ============================================================
            // 目前發布中的題目
            // ============================================================

            var activeQuestions =
                questions
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.CreatedAt)
                    .ToList();

            string currentQuizVersion =
                activeQuestions.Count > 0
                    ? BuildQuizVersion(
                        activeQuestions
                    )
                    : "";


            // ============================================================
            // 題庫版本
            // ============================================================

            var questionVersions =
                new List<QuizQuestionVersion>();


            // 目前實際發布版本
            if (activeQuestions.Count > 0)
            {
                questionVersions.Add(
                    new QuizQuestionVersion
                    {
                        VersionName =
                            string.IsNullOrWhiteSpace(
                                currentQuizVersion)
                                ? "目前版本"
                                : $"目前版本 {currentQuizVersion[..Math.Min(12, currentQuizVersion.Length)]}",

                        Questions =
                            activeQuestions
                                .Select(ToQuizSnapshot)
                                .ToList(),

                        IsCurrent = true,
                        IsTest = false
                    }
                );
            }


            // ============================================================
            // 測試題庫版本 1
            // 正式上線後可刪除
            // ============================================================

            questionVersions.Add(
                new QuizQuestionVersion
                {
                    VersionName =
                        "TEST_VERSION_001",

                    IsCurrent = false,
                    IsTest = true,

                    Questions =
                        new List<QuizQuestionSnapshot>
                        {
                    new QuizQuestionSnapshot
                    {
                        Id = "test_v1_q1",

                        QuestionText =
                            "人體最大的器官為何？",

                        OptionA = "心臟",
                        OptionB = "皮膚",
                        OptionC = "肝臟",
                        OptionD = "肺臟",

                        CorrectAnswer = "B",

                        Explanation =
                            "皮膚是人體最大的器官。"
                    },

                    new QuizQuestionSnapshot
                    {
                        Id = "test_v1_q2",

                        QuestionText =
                            "負責過濾血液並產生尿液的器官為何？",

                        OptionA = "腎臟",
                        OptionB = "肺臟",
                        OptionC = "胃",
                        OptionD = "胰臟",

                        CorrectAnswer = "A",

                        Explanation =
                            "腎臟負責過濾血液並形成尿液。"
                    },

                    new QuizQuestionSnapshot
                    {
                        Id = "test_v1_q3",

                        QuestionText =
                            "人體主要負責氣體交換的器官為何？",

                        OptionA = "肝臟",
                        OptionB = "腎臟",
                        OptionC = "肺臟",
                        OptionD = "脾臟",

                        CorrectAnswer = "C",

                        Explanation =
                            "肺臟是主要的氣體交換器官。"
                    }
                        }
                }
            );


            // ============================================================
            // 測試題庫版本 2
            // 正式上線後可刪除
            // ============================================================

            questionVersions.Add(
                new QuizQuestionVersion
                {
                    VersionName =
                        "TEST_VERSION_002",

                    IsCurrent = false,
                    IsTest = true,

                    Questions =
                        new List<QuizQuestionSnapshot>
                        {
                    new QuizQuestionSnapshot
                    {
                        Id = "test_v2_q1",

                        QuestionText =
                            "心臟主要位於人體哪個腔室？",

                        OptionA = "胸腔",
                        OptionB = "腹腔",
                        OptionC = "骨盆腔",
                        OptionD = "顱腔",

                        CorrectAnswer = "A",

                        Explanation =
                            "心臟位於胸腔中的縱膈。"
                    },

                    new QuizQuestionSnapshot
                    {
                        Id = "test_v2_q2",

                        QuestionText =
                            "肝臟主要位於腹部哪一側？",

                        OptionA = "左下",
                        OptionB = "右上",
                        OptionC = "右下",
                        OptionD = "正中央",

                        CorrectAnswer = "B",

                        Explanation =
                            "肝臟大部分位於右上腹。"
                    },

                    new QuizQuestionSnapshot
                    {
                        Id = "test_v2_q3",

                        QuestionText =
                            "人體主要負責運送氧氣的血球為何？",

                        OptionA = "白血球",
                        OptionB = "血小板",
                        OptionC = "紅血球",
                        OptionD = "淋巴球",

                        CorrectAnswer = "C",

                        Explanation =
                            "紅血球中的血紅素主要負責攜帶氧氣。"
                    },

                    new QuizQuestionSnapshot
                    {
                        Id = "test_v2_q4",

                        QuestionText =
                            "胃主要屬於下列哪一個系統？",

                        OptionA = "呼吸系統",
                        OptionB = "消化系統",
                        OptionC = "泌尿系統",
                        OptionD = "神經系統",

                        CorrectAnswer = "B",

                        Explanation =
                            "胃是消化系統的重要器官。"
                    }
                        }
                }
            );


            // ============================================================
            // 真正歷史作答
            // ============================================================

            var attempts =
                await GetQuizAttempts();

            var archiveSource =
                attempts
                    .Select(attempt =>
                    {
                        List<QuizQuestionSnapshot> snapshots =
                            BuildAttemptQuestionFallback(
                                attempt,
                                questions
                            );

                        string version =
                            !string.IsNullOrWhiteSpace(
                                attempt.QuizVersion)
                                ? attempt.QuizVersion.Trim()
                                : BuildQuizVersionFromSnapshots(
                                    snapshots
                                );

                        return new
                        {
                            Attempt = attempt,
                            Version = version,
                            Questions = snapshots
                        };
                    })
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(
                            x.Version)
                        &&
                        x.Questions.Count > 0
                    )
                    .ToList();


            var archives =
                archiveSource
                    .GroupBy(
                        x => x.Version,
                        StringComparer.OrdinalIgnoreCase
                    )
                    .Where(group =>
                        string.IsNullOrWhiteSpace(
                            currentQuizVersion)
                        ||
                        !string.Equals(
                            group.Key,
                            currentQuizVersion,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    .Select(group =>
                    {
                        var ordered =
                            group
                                .OrderBy(x =>
                                    DateTime.TryParse(
                                        x.Attempt.SubmittedAt,
                                        out DateTime d)
                                        ? d
                                        : DateTime.MinValue
                                )
                                .ToList();

                        var representative =
                            ordered
                                .LastOrDefault(x =>
                                    x.Questions.Count > 0);

                        return new QuizVersionArchive
                        {
                            QuizVersion =
                                group.Key,

                            Questions =
                                representative?.Questions
                                ?? new List<
                                    QuizQuestionSnapshot>(),

                            AttemptCount =
                                group.Count(),

                            FirstSubmittedAt =
                                ordered.FirstOrDefault()?
                                    .Attempt.SubmittedAt
                                ?? "",

                            LastSubmittedAt =
                                ordered.LastOrDefault()?
                                    .Attempt.SubmittedAt
                                ?? ""
                        };
                    })
                    .OrderByDescending(x =>
                        DateTime.TryParse(
                            x.LastSubmittedAt,
                            out DateTime d)
                            ? d
                            : DateTime.MinValue
                    )
                    .ToList();


            ViewBag.CurrentQuizVersion =
                currentQuizVersion;

            ViewBag.QuestionVersions =
                questionVersions;

            ViewBag.QuizArchives =
                archives;

            return View(questions);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateQuizQuestion(
            string questionText,
            string optionA,
            string optionB,
            string optionC,
            string optionD,
            string correctAnswer,
            string explanation,
            bool isActive)
        {
            var block =
                RequireTeacher();

            if (block != null)
                return block;

            questionText =
                questionText?.Trim() ?? "";

            optionA =
                optionA?.Trim() ?? "";

            optionB =
                optionB?.Trim() ?? "";

            optionC =
                optionC?.Trim() ?? "";

            optionD =
                optionD?.Trim() ?? "";

            correctAnswer =
                correctAnswer?
                    .Trim()
                    .ToUpperInvariant()
                ?? "";

            explanation =
                explanation?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(
                    questionText)
                ||
                string.IsNullOrWhiteSpace(optionA)
                ||
                string.IsNullOrWhiteSpace(optionB)
                ||
                string.IsNullOrWhiteSpace(optionC)
                ||
                string.IsNullOrWhiteSpace(optionD))
            {
                TempData["Message"] =
                    "題目與 A、B、C、D 四個選項都必須填寫。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "QuizManage"
                );
            }

            if (!new[] { "A", "B", "C", "D" }
                    .Contains(correctAnswer))
            {
                TempData["Message"] =
                    "正確答案必須為 A、B、C、D 其中一個選項。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "QuizManage"
                );
            }

            var optionValues =
                new[]
                {
                    optionA,
                    optionB,
                    optionC,
                    optionD
                };

            if (optionValues
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase
                    )
                    .Count() != 4)
            {
                TempData["Message"] =
                    "四個選項內容不可重複。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "QuizManage"
                );
            }

            string loginId =
                HttpContext.Session.GetString("LoginId")
                ?? "";

            var question =
                new QuizQuestion
                {
                    Id =
                        Guid.NewGuid()
                            .ToString("N"),

                    QuestionText =
                        questionText,

                    OptionA =
                        optionA,

                    OptionB =
                        optionB,

                    OptionC =
                        optionC,

                    OptionD =
                        optionD,

                    CorrectAnswer =
                        correctAnswer,

                    Explanation =
                        explanation,

                    IsActive =
                        isActive,

                    CreatedBy =
                        loginId,

                    CreatedAt =
                        DateTime.Now.ToString(
                            "yyyy-MM-ddTHH:mm:ss"
                        )
                };

            try
            {
                await PutQuizQuestion(
                    question
                );

                TempData["Message"] =
                    "測驗題目已新增。";

                TempData["MessageType"] =
                    "success";
            }
            catch
            {
                TempData["Message"] =
                    "題目新增失敗。請確認 HBase 的 quiz_question 資料表已建立。";

                TempData["MessageType"] =
                    "error";
            }

            return RedirectToAction(
                "QuizManage"
            );
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleQuizQuestion(
            string id,
            bool isActive)
        {
            var block =
                RequireTeacher();

            if (block != null)
                return block;

            id =
                id?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(id))
            {
                TempData["Message"] =
                    "缺少題目編號。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "QuizManage"
                );
            }

            try
            {
                await SetQuizQuestionActive(
                    id,
                    isActive
                );

                TempData["Message"] =
                    isActive
                        ? "題目已發布。"
                        : "題目已停止發布。";

                TempData["MessageType"] =
                    "success";
            }
            catch
            {
                TempData["Message"] =
                    "更新題目狀態失敗。";

                TempData["MessageType"] =
                    "error";
            }

            return RedirectToAction(
                "QuizManage"
            );
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteQuizQuestion(
            string id)
        {
            var block =
                RequireTeacher();

            if (block != null)
                return block;

            id =
                id?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(id))
            {
                TempData["Message"] =
                    "缺少題目編號。";

                TempData["MessageType"] =
                    "error";

                return RedirectToAction(
                    "QuizManage"
                );
            }

            try
            {
                var response =
                    await _client.DeleteAsync(
                        $"quiz_question/{id}"
                    );


                response.EnsureSuccessStatusCode();

                TempData["Message"] =
                    "題目已刪除。歷史作答紀錄中的題目仍會保留。";

                TempData["MessageType"] =
                    "success";
            }
            catch
            {
                TempData["Message"] =
                    "刪除題目失敗。";

                TempData["MessageType"] =
                    "error";
            }

            return RedirectToAction(
                "QuizManage"
            );
        }


    }
}