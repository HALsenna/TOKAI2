using System;
using System.Collections.Generic;
using System.Linq;
using Daidatsugoku;

/// <summary>SYS-FIELD-001 model checks. Coordinates are global X/Y from the bottom left.</summary>
public static class FieldStageChecks
{
    public static int Run()
    {
        int assertions = 0;
        Action<bool, string> require = (condition, message) =>
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("SYS-FIELD-001: " + message);
        };
        FieldCell[] none = Array.Empty<FieldCell>();
        FieldRow[] tapered = { new(0, 0, 6), new(1, 0, 6), new(2, 1, 5), new(3, 2, 4), new(4, 3, 3) };
        FieldStage irregular = Stage("fixture", 7, 5, tapered, 3, true, none);
        require(FieldStageValidator.Validate(irregular).Count == 0 && FieldStageValidator.TryValidateForPlay(irregular, out _),
            "A contiguous nonrectangular fixture with confirmed empty fences must be playable.");
        require(irregular.CellCount == 23 && irregular.Cells.Count == 23 && new HashSet<FieldCell>(irregular.Cells).Count == 23,
            "Cells must be derived once per actual coordinate, without duplicate or rectangular padding cells.");
        require(irregular.StartCells.Count == 5 && irregular.ExitCells.Count == 5, "Every row must provide its own start and exit.");
        foreach (FieldRow row in tapered)
        {
            require(irregular.TryGetRow(row.Y, out var actual) && actual.StartX == row.StartX && actual.EndX == row.EndX,
                "The global-Y row record must preserve its bounds.");
            require(irregular.IsStart(new FieldCell(row.StartX, row.Y)) && irregular.IsExit(new FieldCell(row.EndX, row.Y)),
                "Starts and exits belong to the row bounds, including rows starting after X=0.");
            for (int x = 0; x < irregular.Width; x++)
                require(irregular.IsInside(new FieldCell(x, row.Y)) == (x >= row.StartX && x <= row.EndX),
                    "Only the row's inclusive contiguous interval may contain cells.");
        }
        require(!irregular.IsInside(new FieldCell(-1, 0)) && !irregular.IsInside(new FieldCell(7, 0)) &&
                !irregular.IsInside(new FieldCell(0, -1)) && !irregular.IsInside(new FieldCell(3, 5)),
            "Coordinates outside nominal bounds must not exist.");
        require(!irregular.TryGetRow(-1, out _) && !irregular.TryGetRow(5, out _), "Missing rows must not alias existing rows.");
        require(new HashSet<FieldCell>(irregular.FenceForbiddenCells).SetEquals(new[] { new FieldCell(2, 3), new FieldCell(3, 4), new FieldCell(4, 3) }),
            "Guard-front bans use each of the three columns' actual highest cell, not one common top row.");
        var guard = new GuardPosition(irregular.GuardColumn, irregular.GuardSide);
        FieldCell[] initialBans = irregular.FenceForbiddenCells.ToArray();
        guard.Flip(); guard.Flip(); guard.Flip();
        require(guard.Side == GuardSide.Bottom && irregular.FenceForbiddenCells.SequenceEqual(initialBans),
            "Runtime guard flips must never relocate the field's fixed initial fence bans.");

        require(irregular.ClassifyMove(new FieldCell(4, 3), new FieldCell(5, 3)) == FieldMoveKind.Escape,
            "Crossing the current row's right edge escapes before the field-wide last column.");
        require(irregular.ClassifyMove(new FieldCell(3, 4), new FieldCell(4, 5)) == FieldMoveKind.Escape,
            "A right-diagonal crossing of the source row exit takes priority even over the top boundary.");
        require(irregular.ClassifyMove(new FieldCell(6, 0), new FieldCell(7, -1)) == FieldMoveKind.Escape,
            "The same right-diagonal escape priority applies at the bottom boundary.");
        require(irregular.ClassifyMove(new FieldCell(2, 3), new FieldCell(2, 4)) == FieldMoveKind.Invalid,
            "A nonexistent target that has not crossed the source row's right edge must be invalid.");
        require(irregular.ClassifyMove(new FieldCell(3, 4), new FieldCell(3, 5)) == FieldMoveKind.Invalid &&
                irregular.ClassifyMove(new FieldCell(0, 0), new FieldCell(0, -1)) == FieldMoveKind.Invalid,
            "A purely vertical move beyond the board must be invalid.");
        require(irregular.ClassifyMove(new FieldCell(3, 3), new FieldCell(4, 3)) == FieldMoveKind.Land &&
                irregular.ClassifyMove(new FieldCell(3, 3), new FieldCell(4, 2)) == FieldMoveKind.Land,
            "An existing destination is a landing; prisoner movement directions remain a separate responsibility.");
        require(irregular.ClassifyMove(new FieldCell(99, 0), new FieldCell(100, 0)) == FieldMoveKind.Invalid,
            "An invalid origin must not manufacture an escape.");
        require(irregular.ClassifyPush(new FieldCell(2, 3), new FieldCell(1, 3)) == FieldMoveKind.ReturnToUnplaced &&
                irregular.ClassifyPush(new FieldCell(4, 3), new FieldCell(5, 3)) == FieldMoveKind.Escape &&
                irregular.ClassifyPush(new FieldCell(3, 3), new FieldCell(2, 3)) == FieldMoveKind.Land,
            "Pushes classify against the occupied row's own left and right boundaries.");

        CheckStageShapes(require);
        CheckValidation(require, tapered);
        CheckSnapshots(require, tapered);
        return assertions;
    }

    private static void CheckStageShapes(Action<bool, string> require)
    {
        // All five serialized stage assets, including approved Stage01, are also checked by catalog integration.
        FieldStage[] drafts =
        {
            Stage("02", 8, 5, new[] { new FieldRow(0, 0, 5), new(1, 0, 6), new(2, 0, 7), new(3, 0, 6), new(4, 0, 5) }, 3, false, null),
            Stage("03", 7, 6, new[] { new FieldRow(0, 0, 3), new(1, 0, 5), new(2, 1, 6), new(3, 1, 6), new(4, 0, 4), new(5, 0, 3) }, 2, false, null),
            Stage("04", 7, 7, new[] { new FieldRow(0, 1, 3), new(1, 0, 4), new(2, 0, 5), new(3, 0, 6), new(4, 0, 5), new(5, 0, 4), new(6, 1, 3) }, 2, false, null),
            Stage("05", 9, 9, new[] { new FieldRow(0, 1, 6), new(1, 1, 7), new(2, 0, 8), new(3, 2, 5), new(4, 3, 5), new(5, 2, 5), new(6, 0, 8), new(7, 1, 7), new(8, 1, 6) }, 4, false, null)
        };
        int[] expected = { 34, 31, 35, 55 };
        FieldCell[][] expectedBans =
        {
            new[] { new FieldCell(2, 4), new(3, 4), new(4, 4) },
            new[] { new FieldCell(1, 5), new(2, 5), new(3, 5) },
            new[] { new FieldCell(1, 6), new(2, 6), new(3, 6) },
            new[] { new FieldCell(3, 8), new(4, 8), new(5, 8) }
        };
        for (int i = 0; i < drafts.Length; i++)
        {
            FieldStage stage = drafts[i];
            require(FieldStageValidator.Validate(stage).Count == 0, "The supplied draft shape must remain structurally valid.");
            require(stage.CellCount == expected[i] && stage.Cells.Count == expected[i], "The provided stage's exact row intervals must produce its documented cell count.");
            require(new HashSet<FieldCell>(stage.FenceForbiddenCells).SetEquals(expectedBans[i]), "The stage's supplied guard column must produce its three documented fence bans.");
            require(!FieldStageValidator.TryValidateForPlay(stage, out var reason) && !string.IsNullOrEmpty(reason), "Unknown initial fences must prevent gameplay while retaining shape data.");
            require(stage.StartCells.Count == stage.Height && stage.ExitCells.Count == stage.Height, "Every draft row must expose a start and exit.");
        }
        require(FieldStageValidator.ValidateCatalog(drafts).Count == 0, "A catalog of distinct structurally valid drafts must validate without pretending the drafts are playable.");
        require(FieldStageValidator.ValidateCatalog(drafts.Concat(new[] { drafts[0] })).Count > 0, "Duplicate stage IDs must be rejected across the catalog.");
    }

    private static void CheckValidation(Action<bool, string> require, FieldRow[] validRows)
    {
        Action<FieldStage, string> invalid = (stage, message) => require(FieldStageValidator.Validate(stage).Count > 0 && !FieldStageValidator.TryValidateForPlay(stage, out _), message);
        invalid(null, "A missing stage is invalid.");
        invalid(Stage("", 7, 5, validRows, 3, true, Array.Empty<FieldCell>()), "Stage ID is required.");
        invalid(new FieldStage("fixture", "", 7, 5, validRows, 3, GuardSide.Top, true, Array.Empty<FieldCell>()), "Stage name is required.");
        invalid(Stage("fixture", 2, 5, Enumerable.Range(0, 5).Select(y => new FieldRow(y, 0, 1)), 1, true, Array.Empty<FieldCell>()), "Width below three must be rejected.");
        invalid(Stage("fixture", 7, 4, validRows.Take(4), 3, true, Array.Empty<FieldCell>()), "Height below five must be rejected.");
        invalid(Stage("fixture", 7, 5, null, 3, true, Array.Empty<FieldCell>()), "Missing row data must not be defaulted to a rectangle.");
        invalid(Stage("fixture", 7, 5, validRows.Take(4), 3, true, Array.Empty<FieldCell>()), "A missing row must not become a hole in an otherwise bounded field.");
        invalid(Stage("fixture", 7, 5, validRows.Concat(new[] { validRows[0] }), 3, true, Array.Empty<FieldCell>()), "Repeated Y records must not be merged or silently corrected.");
        invalid(Stage("fixture", 7, 5, validRows.Skip(1).Concat(new[] { new FieldRow(-1, 0, 6) }), 3, true, Array.Empty<FieldCell>()), "Negative row coordinates must be rejected.");
        invalid(Stage("fixture", 7, 5, validRows.Take(4).Concat(new[] { new FieldRow(5, 3, 3) }), 3, true, Array.Empty<FieldCell>()), "Rows outside the height must be rejected.");
        invalid(Stage("fixture", 7, 5, validRows.Take(4).Concat(new[] { new FieldRow(4, -1, 3) }), 3, true, Array.Empty<FieldCell>()), "Negative X bounds must be rejected.");
        invalid(Stage("fixture", 7, 5, validRows.Take(4).Concat(new[] { new FieldRow(4, 3, 7) }), 3, true, Array.Empty<FieldCell>()), "X bounds must fit within width.");
        invalid(Stage("fixture", 7, 5, validRows.Take(4).Concat(new[] { new FieldRow(4, 4, 3) }), 3, true, Array.Empty<FieldCell>()), "Reversed row intervals must be rejected.");
        invalid(new FieldStage("fixture", "fixture", 7, 5, validRows, 3, GuardSide.Bottom, true, Array.Empty<FieldCell>()), "Stages must not start with a BOTTOM guard.");
        invalid(new FieldStage("fixture", "fixture", 7, 5, validRows, 3, (GuardSide)99, true, Array.Empty<FieldCell>()), "Undefined guard sides must be rejected.");
        invalid(Stage("fixture", 7, 5, validRows, -1, true, Array.Empty<FieldCell>()), "Negative guard columns must be rejected.");
        invalid(Stage("fixture", 7, 5, validRows, 7, true, Array.Empty<FieldCell>()), "Guard columns outside width must be rejected.");
        invalid(Stage("fixture", 7, 5, Enumerable.Range(0, 5).Select(y => new FieldRow(y, 0, 2)), 4, true, Array.Empty<FieldCell>()), "Each of the guard-front three columns must contain a highest cell.");
        invalid(Stage("fixture", 7, 5, validRows, 3, true, new[] { new FieldCell(0, 4) }), "Initial fences must belong to real cells.");
        invalid(Stage("fixture", 7, 5, validRows, 3, true, new[] { new FieldCell(3, 4) }), "Initial fences may not occupy a fixed forbidden cell.");
        var repeated = new[] { new FieldCell(0, 0), new FieldCell(0, 0) };
        invalid(Stage("fixture", 7, 5, validRows, 3, true, repeated), "Duplicate initial fences must be rejected rather than deduplicated.");
        invalid(Stage("fixture", 7, 5, validRows, 3, true, null), "Confirmed-but-missing initial fence data must be invalid.");
        FieldStage pendingEmpty = Stage("pending", 7, 5, validRows, 3, false, Array.Empty<FieldCell>());
        require(FieldStageValidator.Validate(pendingEmpty).Count == 0 && !FieldStageValidator.TryValidateForPlay(pendingEmpty, out _), "An empty pending fence list must not be interpreted as confirmed no-fence gameplay.");
        FieldStage large = Stage("large", 40, 60, Enumerable.Range(0, 60).Select(y => new FieldRow(y, 0, 39)), 20, true, Array.Empty<FieldCell>());
        require(FieldStageValidator.Validate(large).Count == 0 && large.CellCount == 2400, "The five supplied shapes must not impose an invented maximum map size.");
    }

    private static void CheckSnapshots(Action<bool, string> require, FieldRow[] rows)
    {
        var sourceRows = rows.ToList(); var sourceFences = new List<FieldCell> { new(0, 0) };
        FieldStage stage = Stage("snapshot", 7, 5, sourceRows, 3, true, sourceFences);
        sourceRows.Clear(); sourceFences[0] = new FieldCell(3, 4);
        require(stage.Rows.Count == 5 && stage.InitialFences[0].Equals(new FieldCell(0, 0)) && stage.CellCount == 23,
            "A stage snapshot must not change when its caller later edits the source lists.");
        FieldRow copy = stage.Rows[0]; copy.StartX = 6;
        FieldCell fenceCopy = stage.InitialFences[0]; fenceCopy.X = 5;
        require(stage.IsStart(new FieldCell(0, 0)) && stage.InitialFences[0].Equals(new FieldCell(0, 0)), "Value copies must not mutate stage row or fence records.");
        FieldCell[] bans = stage.FenceForbiddenCells.ToArray();
        FieldStageValidator.Validate(stage);
        require(stage.InitialFences.Count == 1 && stage.InitialFences[0].Equals(new FieldCell(0, 0)) && stage.FenceForbiddenCells.SequenceEqual(bans),
            "Validation must not remove or relocate source fences or derived bans.");
    }

    private static FieldStage Stage(string id, int width, int height, IEnumerable<FieldRow> rows, int guard,
        bool fencesDefined, IEnumerable<FieldCell> fences) => new(id, id, width, height, rows, guard, GuardSide.Top, fencesDefined, fences);
}
