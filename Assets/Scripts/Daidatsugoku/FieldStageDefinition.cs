using System.Collections.Generic;
using UnityEngine;

namespace Daidatsugoku
{
    /// <summary>Stage authoring asset; Resources/FieldStages assets form the stage catalog.</summary>
    [CreateAssetMenu(menuName = "Daidatsugoku/Field Stage")]
    public sealed class FieldStageDefinition : ScriptableObject
    {
        [SerializeField] private string stageId;
        [SerializeField] private string stageName;
        [SerializeField] private int width;
        [SerializeField] private int height;
        [SerializeField] private List<FieldRow> rows = new();
        [SerializeField] private int guardColumn;
        [SerializeField] private GuardSide guardSide = GuardSide.Top;
        [SerializeField] private bool initialFencesDefined;
        [SerializeField] private List<FieldCell> initialFences = new();
        [SerializeField, TextArea] private string sourceIssue;
        [SerializeField] private bool includedInSelection = true;
        public string SourceIssue => sourceIssue;
        public bool IncludedInSelection => includedInSelection;

        public FieldStage ToStage() => new(stageId, stageName, width, height, rows,
            guardColumn, guardSide, initialFencesDefined, initialFences);

        public void Configure(FieldStage stage, string unresolvedSourceIssue = null, bool includeInSelection = true)
        {
            stageId = stage.StageId; stageName = stage.StageName; width = stage.Width; height = stage.Height;
            rows = stage.Rows == null ? null : new List<FieldRow>(stage.Rows);
            guardColumn = stage.GuardColumn; guardSide = stage.GuardSide;
            initialFencesDefined = stage.InitialFencesDefined;
            initialFences = stage.InitialFences == null ? null : new List<FieldCell>(stage.InitialFences);
            sourceIssue = unresolvedSourceIssue;
            includedInSelection = includeInSelection;
        }
    }
}
