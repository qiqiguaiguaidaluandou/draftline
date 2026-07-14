using Draftline.Core.Contracts;
using Draftline.Core.Schemas;
using Draftline.Gateway.AntiCorruption;

namespace Draftline.Tests.AntiCorruption;

/// <summary>
/// 挑图→EBS 机加结果回传（CUX_AI_MACH_DRW_RST）的批次级响应 → 逐行结果映射。
/// 规则（对方明确）：任何 E 的 X_RESPONSE_DATA 都会带上未成功的数据，但**只有**「数据更新部分失败」这一类
/// 才把这些行入异常池、其余算已落库成功；其余任何 E 一律整批回滚（抛异常、可重试）。判据是 X_RETURN_MESG。
/// </summary>
public class EbsDrawingResultTests
{
    private static SubmitRow Row(string ebsId) => new()
    {
        RowKey = ebsId,
        Values = new() { [FieldSchemas.DrawingKeys.EbsId] = ebsId },
    };

    private static IReadOnlyList<SubmitRow> Rows(params string[] ids) =>
        ids.Select(Row).ToList();

    [Fact]
    public void ReturnCode_S_marks_every_row_success()
    {
        // 真实成功报文：X_RETURN_MESG=null、X_RESPONSE_DATA=null，且带 Oracle 的 @xmlns 字段（应被忽略）。
        var raw = """
        {
          "OutputParameters": {
            "@xmlns": "http://xmlns.oracle.com/apps/cux/rest/WS/invokefmsws/",
            "@xmlns:xsi": "http://www.w3.org/2001/XMLSchema-instance",
            "X_BATCH_NUMBER": "AI20260622163533",
            "X_RETURN_CODE": "S",
            "X_RETURN_MESG": null,
            "X_RESPONSE_DATA": null
          }
        }
        """;

        var res = RemoteSubmitSink.MapDrawingResult(Rows("1018953", "1018954"), raw);

        Assert.All(res, r => Assert.True(r.Success));
        Assert.All(res, r => Assert.Null(r.Message));
    }

    [Fact]
    public void PartialFailure_uses_per_row_err_msg_as_exception_reason()
    {
        // 真实部分失败报文：E + "数据更新部分失败！" + X_RESPONSE_DATA 是转义 JSON 字符串数组（回传失败的行）。
        // 每行带自己的 ERR_MSG → 该行异常原因用这条逐行原因，而非批次级 X_RETURN_MESG。
        var raw = """
        {
          "OutputParameters": {
            "@xmlns": "http://xmlns.oracle.com/apps/cux/rest/WS/invokefmsws/",
            "@xmlns:xsi": "http://www.w3.org/2001/XMLSchema-instance",
            "X_BATCH_NUMBER": "AI20260622163533",
            "X_RETURN_CODE": "E",
            "X_RETURN_MESG": "数据更新部分失败！",
            "X_RESPONSE_DATA": "[{\"ERR_MSG\":\"SEQ_ID数据不正确,未找到相关数据！\",\"ORG_CODE\":\"H06\",\"SEQ_ID\":\"1018953\"},{\"ERR_MSG\":\"组织不存在\",\"ORG_CODE\":\"H06\",\"SEQ_ID\":\"1018954\"}]"
          }
        }
        """;

        var res = RemoteSubmitSink.MapDrawingResult(Rows("1018953", "1018954", "1018955"), raw);

        var f1 = res.Single(r => r.RowKey == "1018953");
        var f2 = res.Single(r => r.RowKey == "1018954");
        Assert.False(f1.Success);                                     // 列出 → 失败（进异常池）
        Assert.False(f2.Success);
        Assert.Equal("SEQ_ID数据不正确,未找到相关数据！", f1.Message); // 逐行 ERR_MSG 写进异常记录
        Assert.Equal("组织不存在", f2.Message);
        Assert.True(res.Single(r => r.RowKey == "1018955").Success); // 未列出 → 已落库成功
    }

    [Fact]
    public void PartialFailure_falls_back_to_batch_mesg_when_err_msg_absent()
    {
        // 没有 ERR_MSG（旧报文形态）→ 回落到批次级 X_RETURN_MESG。
        var raw = """
        {
          "OutputParameters": {
            "X_RETURN_CODE": "E",
            "X_RETURN_MESG": "数据更新部分失败！",
            "X_RESPONSE_DATA": "[{\"SEQ_ID\":1018953, \"ORG_CODE\":\"H06\"}]"
          }
        }
        """;

        var res = RemoteSubmitSink.MapDrawingResult(Rows("1018953", "1018954"), raw);

        Assert.False(res.Single(r => r.RowKey == "1018953").Success);
        Assert.Equal("数据更新部分失败！", res.Single(r => r.RowKey == "1018953").Message);
        Assert.True(res.Single(r => r.RowKey == "1018954").Success);
    }

    [Fact]
    public void PartialFailure_also_supports_array_form_response_data()
    {
        // 容错第二形态：X_RESPONSE_DATA 直接是数组（非转义字符串），SEQ_ID 为数字。仍走部分失败分流。
        var raw = """
        {
          "OutputParameters": {
            "X_RETURN_CODE": "E",
            "X_RETURN_MESG": "数据更新部分失败！",
            "X_RESPONSE_DATA": [ { "SEQ_ID": 1018953, "ORG_CODE": "H06" } ]
          }
        }
        """;

        var res = RemoteSubmitSink.MapDrawingResult(Rows("1018953", "1018954"), raw);

        Assert.False(res.Single(r => r.RowKey == "1018953").Success);
        Assert.True(res.Single(r => r.RowKey == "1018954").Success);
    }

    [Fact]
    public void NonPartial_E_rolls_back_even_when_response_data_carries_rows()
    {
        // 关键新规则：非「部分失败」的 E，即使 X_RESPONSE_DATA 也带回了行数据，也一律整批回滚 → 抛异常（可重试）。
        var raw = """
        {
          "OutputParameters": {
            "X_RETURN_CODE": "E",
            "X_RETURN_MESG": "系统异常，全部回滚",
            "X_RESPONSE_DATA": "[{\"SEQ_ID\":1018953, \"ORG_CODE\":\"H06\"}]"
          }
        }
        """;

        Assert.Throws<InvalidOperationException>(() =>
            RemoteSubmitSink.MapDrawingResult(Rows("1018953", "1018954"), raw));
    }

    [Fact]
    public void PLS_package_error_rolls_back_retryable()
    {
        // 文档里的 PLS 包未部署那种系统性报错：E + 非部分失败文案 + X_RESPONSE_DATA=null → 抛异常（可重试）。
        var raw = """
        { "OutputParameters": { "X_RETURN_CODE": "E", "X_RETURN_MESG": "PLS-00302 ...", "X_RESPONSE_DATA": null } }
        """;

        Assert.Throws<InvalidOperationException>(() =>
            RemoteSubmitSink.MapDrawingResult(Rows("1018953"), raw));
    }
}
