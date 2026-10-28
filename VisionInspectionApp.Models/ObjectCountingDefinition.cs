namespace VisionInspectionApp.Models;

public enum ObjectCountingAlgorithm
{
    ShapeMatching = 0,
    NormalizedCorrelation = 1
}

public sealed class ObjectCountingDefinition
{
    public string Name { get; set; } = string.Empty;

    public Roi SearchRoi { get; set; } = new();

    public Roi TemplateRoi { get; set; } = new();

    public string TemplateImageFile { get; set; } = string.Empty;

    /// <summary>
    /// Số lượng vật thể danh định (chuẩn/mục tiêu) cần đếm.
    /// </summary>
    public int NominalCount { get; set; } = 1;

    /// <summary>
    /// Dung sai dương (cho phép vượt quá bao nhiêu vật thể so với NominalCount).
    /// </summary>
    public int TolerancePlus { get; set; } = 0;

    /// <summary>
    /// Dung sai âm (cho phép thiếu bao nhiêu vật thể so với NominalCount).
    /// </summary>
    public int ToleranceMinus { get; set; } = 0;

    /// <summary>
    /// Thuộc tính tiện ích: đọc dung sai đối xứng (TolerancePlus) hoặc gán cùng lúc cả TolerancePlus và ToleranceMinus.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int Tolerance
    {
        get => Math.Max(TolerancePlus, ToleranceMinus);
        set
        {
            var v = Math.Max(0, value);
            TolerancePlus = v;
            ToleranceMinus = v;
        }
    }

    /// <summary>
    /// Số lượng vật thể tối thiểu cho phép để đạt PASS.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int MinCount => Math.Max(0, NominalCount - ToleranceMinus);

    /// <summary>
    /// Số lượng vật thể tối đa cho phép để đạt PASS.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int MaxCount => NominalCount + TolerancePlus;

    /// <summary>
    /// Ngưỡng điểm tin cậy tương đồng (0.1 đến 1.0, ví dụ 0.65 = 65%).
    /// </summary>
    public double MinScore { get; set; } = 0.65;

    /// <summary>
    /// Góc quay tối thiểu để quét (độ), mặc định -180°.
    /// </summary>
    public double MinAngle { get; set; } = -180.0;

    /// <summary>
    /// Góc quay tối đa để quét (độ), mặc định +180°.
    /// </summary>
    public double MaxAngle { get; set; } = 180.0;

    /// <summary>
    /// Bước góc quét thô (độ), mặc định 3.0°.
    /// </summary>
    public double AngleStep { get; set; } = 3.0;

    /// <summary>
    /// Ngưỡng diện tích chồng lấp tối đa giữa 2 phát hiện để loại bỏ trùng lặp (NMS), mặc định 0.30.
    /// </summary>
    public double MaxOverlap { get; set; } = 0.30;

    /// <summary>
    /// Giới hạn số lượng vật thể tối đa cần tìm để tránh vòng lặp quá tải, mặc định 100.
    /// </summary>
    public int MaxFoundLimit { get; set; } = 100;

    /// <summary>
    /// Thuật toán tìm kiếm và so khớp (Mặc định: NormalizedCorrelation - độ chính xác cao, loại bỏ nhiễu vân nền).
    /// </summary>
    public ObjectCountingAlgorithm Algorithm { get; set; } = ObjectCountingAlgorithm.NormalizedCorrelation;
}
