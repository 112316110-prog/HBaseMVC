namespace HBaseMVC.Models
{
    public class StudentDailyNote
    {
        public string RowKey { get; set; } = "";
        public string StudentId { get; set; } = "";
        public string NoteDate { get; set; } = "";
        public string Content { get; set; } = "";
        public string UpdatedAt { get; set; } = "";
    }

    public class DailyNoteSurgeryDate
    {
        public string Date { get; set; } = "";
        public int ScheduleCount { get; set; } = 0;
        public List<string> CadaverRowKeys { get; set; } = new();

        public string DisplayText
        {
            get
            {
                string cadavers =
                    string.Join(
                        "、",
                        CadaverRowKeys
                            .Where(x =>
                                !string.IsNullOrWhiteSpace(x))
                            .Distinct(
                                StringComparer.OrdinalIgnoreCase
                            )
                    );

                if (ScheduleCount <= 1)
                {
                    return string.IsNullOrWhiteSpace(cadavers)
                        ? Date
                        : $"{Date}｜大體老師 {cadavers}";
                }

                return string.IsNullOrWhiteSpace(cadavers)
                    ? $"{Date}｜{ScheduleCount} 個手術安排"
                    : $"{Date}｜{ScheduleCount} 個手術安排｜{cadavers}";
            }
        }
    }

    public class StudentDailyNotesViewModel
    {
        public string SelectedDate { get; set; } = "";
        public string CurrentContent { get; set; } = "";
        public string CurrentUpdatedAt { get; set; } = "";
        public List<StudentDailyNote> Notes { get; set; } = new();
        public List<DailyNoteSurgeryDate> AvailableDates { get; set; } = new();
    }
}