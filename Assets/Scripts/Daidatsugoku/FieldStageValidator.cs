using System;
using System.Collections.Generic;

namespace Daidatsugoku
{
    /// <summary>Rejects invalid data without repairing shapes, fences or unresolved configuration.</summary>
    public static class FieldStageValidator
    {
        public static IReadOnlyList<string> Validate(FieldStage stage)
        {
            var errors = new List<string>();
            if (stage == null) { errors.Add("ステージデータがありません。"); return errors.AsReadOnly(); }
            if (string.IsNullOrWhiteSpace(stage.StageId)) errors.Add("stageIdが未設定です。");
            if (string.IsNullOrWhiteSpace(stage.StageName)) errors.Add("stageNameが未設定です。");
            if (stage.Width < 3) errors.Add("フィールドの列数は3以上必要です。");
            if (stage.Height < 5) errors.Add("フィールドの行数は5以上必要です。");
            if (stage.GuardSide != GuardSide.Top) errors.Add("看守の初期方向はTOPである必要があります。");
            if (stage.GuardColumn < 0 || stage.GuardColumn >= stage.Width) errors.Add("guardColumnが列範囲外です。");

            var rows = new Dictionary<int, FieldRow>();
            if (stage.Rows == null) errors.Add("rowsが未設定です。");
            else
            {
                if (stage.Rows.Count != stage.Height) errors.Add("すべてのY座標に行データを1件ずつ定義してください。");
                foreach (FieldRow row in stage.Rows)
                {
                    if (row.Y < 0 || row.Y >= stage.Height) errors.Add($"行Y={row.Y}がフィールドの範囲外です。");
                    if (row.StartX < 0 || row.EndX < row.StartX || row.EndX >= stage.Width)
                        errors.Add($"行Y={row.Y}の開始X・終了Xが不正です。");
                    if (rows.ContainsKey(row.Y)) errors.Add($"行Y={row.Y}が重複しています。");
                    else rows.Add(row.Y, row);
                }
            }

            var forbidden = new HashSet<FieldCell>();
            for (int offset = -1; offset <= 1; offset++)
            {
                long column = (long)stage.GuardColumn + offset;
                bool found = false; int top = -1;
                if (column >= 0 && column < stage.Width)
                    foreach (FieldRow row in rows.Values)
                        if (row.Y >= 0 && row.Y < stage.Height && row.StartX <= column && column <= row.EndX && row.Y > top)
                        { top = row.Y; found = true; }
                if (!found) errors.Add($"看守正面の対象列X={column}に最上段マスが存在しません。");
                else forbidden.Add(new FieldCell((int)column, top));
            }

            if (stage.InitialFencesDefined && stage.InitialFences == null)
                errors.Add("確定済みのinitialFencesが欠落しています。柵なしの場合も空配列を明示してください。");
            if (stage.InitialFences != null)
            {
                var fences = new HashSet<FieldCell>();
                foreach (FieldCell fence in stage.InitialFences)
                {
                    if (!fences.Add(fence)) errors.Add($"初期柵{fence}が重複しています。");
                    if (!rows.TryGetValue(fence.Y, out FieldRow row) || fence.X < 0 || fence.X >= stage.Width ||
                        fence.Y < 0 || fence.Y >= stage.Height || fence.X < row.StartX || fence.X > row.EndX)
                        errors.Add($"初期柵{fence}は存在しないマスです。");
                    if (forbidden.Contains(fence)) errors.Add($"初期柵{fence}が柵設置禁止位置と重複しています。");
                }
            }
            return errors.AsReadOnly();
        }

        public static bool TryValidateForPlay(FieldStage stage, out string reason)
        {
            IReadOnlyList<string> errors = Validate(stage);
            if (errors.Count > 0) { reason = string.Join(" / ", errors); return false; }
            if (!stage.InitialFencesDefined)
            {
                reason = "初期柵が未確定のため、このステージは対局を開始できません。";
                return false;
            }
            reason = null; return true;
        }

        public static IReadOnlyList<string> ValidateCatalog(IEnumerable<FieldStage> stages)
        {
            var errors = new List<string>();
            if (stages == null) { errors.Add("ステージ一覧がありません。"); return errors.AsReadOnly(); }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (FieldStage stage in stages)
            {
                if (stage != null && !string.IsNullOrWhiteSpace(stage.StageId) && !ids.Add(stage.StageId))
                    errors.Add($"stageId={stage.StageId}が重複しています。");
                foreach (string error in Validate(stage)) errors.Add(stage == null ? error : $"[{stage.StageId}] {error}");
            }
            return errors.AsReadOnly();
        }
    }
}
