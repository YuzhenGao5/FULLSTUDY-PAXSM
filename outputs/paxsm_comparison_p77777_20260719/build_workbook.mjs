import fs from "node:fs/promises";
import { SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const sourceCsv = String.raw`C:\Users\ygao930\OneDrive - The University of Auckland\Desktop\CARE-XR Data\P77777\20260719T072528Z_be6915\paxsm-comparison\PAXSMComparison_Data\PAXSMComparison_AnalysisReady_P77777_20260719_193524_completed.csv`;
const outputDir = String.raw`C:\Users\ygao930\Documents\GitHub\Fullstudy\My project (3)\outputs\paxsm_comparison_p77777_20260719`;
const outputXlsx = `${outputDir}\\PAXSMComparison_P77777_AnalysisReady.xlsx`;
const previewSummary = `${outputDir}\\summary_preview.png`;
const previewRaw = `${outputDir}\\analysis_ready_preview.png`;

const csvText = await fs.readFile(sourceCsv, "utf8");
const workbook = await Workbook.fromCSV(csvText, { sheetName: "Analysis Ready" });
const raw = workbook.worksheets.getItem("Analysis Ready");
const rawMatrix = raw.getUsedRange().values;
const headers = rawMatrix[0].map((value) => String(value ?? "").replace(/^\uFEFF/, ""));

function columnLetter(index) {
  let n = index + 1;
  let result = "";
  while (n > 0) {
    const remainder = (n - 1) % 26;
    result = String.fromCharCode(65 + remainder) + result;
    n = Math.floor((n - 1) / 26);
  }
  return result;
}

function headerIndex(name) {
  const index = headers.indexOf(name);
  if (index < 0) throw new Error(`Missing source column: ${name}`);
  return index;
}

const methodColumn = headerIndex("method");
const paxsmRow = rawMatrix.findIndex((row, index) => index > 0 && row[methodColumn] === "paxsm") + 1;
const pointClickRow = rawMatrix.findIndex((row, index) => index > 0 && row[methodColumn] === "point_click") + 1;
if (paxsmRow < 2 || pointClickRow < 2) throw new Error("Could not locate both comparison methods.");

function sourceCell(columnName, rowNumber) {
  return `'Analysis Ready'!${columnLetter(headerIndex(columnName))}${rowNumber}`;
}

function sourceFormula(columnName, rowNumber) {
  return `=${sourceCell(columnName, rowNumber)}`;
}

const summary = workbook.worksheets.add("Comparison Summary");
summary.showGridLines = false;
summary.freezePanes.freezeRows(6);

summary.mergeCells("A1:G1");
summary.getRange("A1:G1").values = [["PAXSM Comparison Study | Participant Summary"]];
summary.getRange("A1:G1").format = {
  fill: "#1F4E78",
  font: { name: "Aptos Display", size: 18, bold: true, color: "#FFFFFF" },
  verticalAlignment: "center",
};
summary.getRange("A1:G1").format.rowHeight = 34;

summary.getRange("A3:H4").values = [
  ["Participant", null, "Run ID", null, "Sequence", null, "Session", null],
  ["Generated (UTC)", null, "PAXSM quality", null, "Point-click quality", null, "Export reason", null],
];
summary.getRange("B3").formulas = [[sourceFormula("participantId", paxsmRow)]];
summary.getRange("D3").formulas = [[sourceFormula("runId", paxsmRow)]];
summary.getRange("F3").formulas = [[sourceFormula("sequenceCode", paxsmRow)]];
summary.getRange("H3").formulas = [[sourceFormula("sessionNumber", paxsmRow)]];
summary.getRange("B4").formulas = [[sourceFormula("generatedUtc", paxsmRow)]];
summary.getRange("D4").formulas = [[sourceFormula("qualityFlags", paxsmRow)]];
summary.getRange("F4").formulas = [[sourceFormula("qualityFlags", pointClickRow)]];
summary.getRange("H4").formulas = [[sourceFormula("exportReason", paxsmRow)]];
summary.getRange("B4").format.numberFormat = "yyyy-mm-dd hh:mm:ss";
summary.getRange("A3:H4").format.wrapText = true;
summary.getRange("A3:H4").format.rowHeight = 30;
summary.getRange("A3:H4").format = {
  font: { name: "Aptos", size: 10 },
  verticalAlignment: "center",
  borders: { preset: "inside", style: "thin", color: "#D9E2F3" },
};
summary.getRange("A3:A4").format.font = { name: "Aptos", size: 10, bold: true, color: "#1F1F1F" };
summary.getRange("C3:C4").format.font = { name: "Aptos", size: 10, bold: true, color: "#1F1F1F" };
summary.getRange("E3:E4").format.font = { name: "Aptos", size: 10, bold: true, color: "#1F1F1F" };
summary.getRange("G3:G4").format.font = { name: "Aptos", size: 10, bold: true, color: "#1F1F1F" };
summary.getRange("A3:A4").format.fill = "#D9EAF7";
summary.getRange("C3:C4").format.fill = "#D9EAF7";
summary.getRange("E3:E4").format.fill = "#D9EAF7";
summary.getRange("G3:G4").format.fill = "#D9EAF7";

summary.mergeCells("A6:E6");
summary.getRange("A6:E6").values = [["Core Comparison Outcomes"]];
summary.getRange("A6:E6").format = {
  fill: "#4472C4",
  font: { name: "Aptos", size: 12, bold: true, color: "#FFFFFF" },
};
summary.getRange("A7:E7").values = [["Metric", "PAXSM", "Point-and-click", "Difference", "Interpretation / unit"]];

const coreMetrics = [
  ["Input accuracy", "inputAccuracy", "percentage"],
  ["Input mean answer RT", "inputMeanAnswerRt", "seconds"],
  ["SUS score", "susScore", "0-100"],
  ["Easy task accuracy", "easyTaskAccuracy", "percentage"],
  ["Hard task accuracy", "hardTaskAccuracy", "percentage"],
  ["Easy task mean decision RT", "easyTaskMeanDecisionRt", "seconds"],
  ["Hard task mean decision RT", "hardTaskMeanDecisionRt", "seconds"],
  ["Easy NASA-TLX raw mean", "easyNasaRawMean", "1-21"],
  ["Hard NASA-TLX raw mean", "hardNasaRawMean", "1-21"],
  ["Easy mean confidence", "easyConfidenceMean", "1-5"],
  ["Hard mean confidence", "hardConfidenceMean", "1-5"],
  ["Easy questionnaire total time", "easyQuestionnaireTotalRt", "seconds"],
  ["Hard questionnaire total time", "hardQuestionnaireTotalRt", "seconds"],
  ["Input incomplete items", "inputIncompleteItems", "count"],
];

const coreStartRow = 8;
summary.getRangeByIndexes(coreStartRow - 1, 0, coreMetrics.length, 1).values = coreMetrics.map(([label]) => [label]);
summary.getRangeByIndexes(coreStartRow - 1, 4, coreMetrics.length, 1).values = coreMetrics.map(([, , unit]) => [unit]);
summary.getRangeByIndexes(coreStartRow - 1, 1, coreMetrics.length, 1).formulas = coreMetrics.map(([, column]) => [sourceFormula(column, paxsmRow)]);
summary.getRangeByIndexes(coreStartRow - 1, 2, coreMetrics.length, 1).formulas = coreMetrics.map(([, column]) => [sourceFormula(column, pointClickRow)]);
summary.getRangeByIndexes(coreStartRow - 1, 3, coreMetrics.length, 1).formulas = coreMetrics.map((_, index) => [`=B${coreStartRow + index}-C${coreStartRow + index}`]);

const coreEndRow = coreStartRow + coreMetrics.length - 1;
summary.getRange(`A7:E${coreEndRow}`).format = {
  font: { name: "Aptos", size: 10 },
  borders: { preset: "inside", style: "thin", color: "#D9E2F3" },
  verticalAlignment: "center",
};
summary.getRange("A7:E7").format = {
  fill: "#D9EAF7",
  font: { name: "Aptos", size: 10, bold: true, color: "#1F1F1F" },
  borders: { preset: "inside", style: "thin", color: "#9FBAD0" },
};
summary.getRange(`B${coreStartRow + 1}:D${coreEndRow - 1}`).format.numberFormat = "0.000";
summary.getRange(`B${coreStartRow}:D${coreEndRow}`).format.horizontalAlignment = "right";
summary.getRange(`B${coreStartRow}:D${coreStartRow}`).format.numberFormat = "0.0%";
summary.getRange(`B${coreStartRow + 3}:D${coreStartRow + 4}`).format.numberFormat = "0.0%";
summary.getRange(`B${coreEndRow}:D${coreEndRow}`).format.numberFormat = "0";

const nasaTitleRow = coreEndRow + 3;
summary.mergeCells(`A${nasaTitleRow}:G${nasaTitleRow}`);
summary.getRange(`A${nasaTitleRow}:G${nasaTitleRow}`).values = [["NASA-TLX Dimension Ratings"]];
summary.getRange(`A${nasaTitleRow}:G${nasaTitleRow}`).format = {
  fill: "#4472C4",
  font: { name: "Aptos", size: 12, bold: true, color: "#FFFFFF" },
};
summary.getRange(`A${nasaTitleRow + 1}:G${nasaTitleRow + 1}`).values = [[
  "Dimension", "Easy PAXSM", "Easy point-click", "Easy difference",
  "Hard PAXSM", "Hard point-click", "Hard difference",
]];

const dimensions = [
  ["Mental demand", "Mental"],
  ["Physical demand", "Physical"],
  ["Temporal demand", "Temporal"],
  ["Performance", "Performance"],
  ["Effort", "Effort"],
  ["Frustration", "Frustration"],
];
const nasaStartRow = nasaTitleRow + 2;
summary.getRangeByIndexes(nasaStartRow - 1, 0, dimensions.length, 1).values = dimensions.map(([label]) => [label]);
summary.getRangeByIndexes(nasaStartRow - 1, 1, dimensions.length, 1).formulas = dimensions.map(([, suffix]) => [sourceFormula(`easyNasa${suffix}`, paxsmRow)]);
summary.getRangeByIndexes(nasaStartRow - 1, 2, dimensions.length, 1).formulas = dimensions.map(([, suffix]) => [sourceFormula(`easyNasa${suffix}`, pointClickRow)]);
summary.getRangeByIndexes(nasaStartRow - 1, 3, dimensions.length, 1).formulas = dimensions.map((_, index) => [`=B${nasaStartRow + index}-C${nasaStartRow + index}`]);
summary.getRangeByIndexes(nasaStartRow - 1, 4, dimensions.length, 1).formulas = dimensions.map(([, suffix]) => [sourceFormula(`hardNasa${suffix}`, paxsmRow)]);
summary.getRangeByIndexes(nasaStartRow - 1, 5, dimensions.length, 1).formulas = dimensions.map(([, suffix]) => [sourceFormula(`hardNasa${suffix}`, pointClickRow)]);
summary.getRangeByIndexes(nasaStartRow - 1, 6, dimensions.length, 1).formulas = dimensions.map((_, index) => [`=E${nasaStartRow + index}-F${nasaStartRow + index}`]);
const nasaEndRow = nasaStartRow + dimensions.length - 1;
summary.getRange(`A${nasaTitleRow + 1}:G${nasaEndRow}`).format = {
  font: { name: "Aptos", size: 10 },
  borders: { preset: "inside", style: "thin", color: "#D9E2F3" },
  verticalAlignment: "center",
};
summary.getRange(`A${nasaTitleRow + 1}:G${nasaTitleRow + 1}`).format = {
  fill: "#D9EAF7",
  font: { name: "Aptos", size: 10, bold: true, color: "#1F1F1F" },
  borders: { preset: "inside", style: "thin", color: "#9FBAD0" },
};
summary.getRange(`B${nasaStartRow}:G${nasaEndRow}`).format.numberFormat = "0.0";

const completenessTitleRow = nasaEndRow + 3;
summary.mergeCells(`A${completenessTitleRow}:D${completenessTitleRow}`);
summary.getRange(`A${completenessTitleRow}:D${completenessTitleRow}`).values = [["Collection Completeness"]];
summary.getRange(`A${completenessTitleRow}:D${completenessTitleRow}`).format = {
  fill: "#70AD47",
  font: { name: "Aptos", size: 12, bold: true, color: "#FFFFFF" },
};
summary.getRange(`A${completenessTitleRow + 1}:D${completenessTitleRow + 1}`).values = [["Check", "PAXSM", "Point-and-click", "Expected"]];
const completeness = [
  ["Input items", "inputItems", "3"],
  ["SUS answered items", "susAnsweredItems", "10"],
  ["Easy NASA-TLX items", "easyNasaItems", "6"],
  ["Easy confidence complete", "easyConfidenceComplete", "1"],
  ["Hard NASA-TLX items", "hardNasaItems", "6"],
  ["Hard confidence complete", "hardConfidenceComplete", "1"],
  ["Method complete", "methodComplete", "1"],
];
const completenessStartRow = completenessTitleRow + 2;
summary.getRangeByIndexes(completenessStartRow - 1, 0, completeness.length, 1).values = completeness.map(([label]) => [label]);
summary.getRangeByIndexes(completenessStartRow - 1, 1, completeness.length, 1).formulas = completeness.map(([, column]) => [sourceFormula(column, paxsmRow)]);
summary.getRangeByIndexes(completenessStartRow - 1, 2, completeness.length, 1).formulas = completeness.map(([, column]) => [sourceFormula(column, pointClickRow)]);
summary.getRangeByIndexes(completenessStartRow - 1, 3, completeness.length, 1).values = completeness.map(([, , expected]) => [Number(expected)]);
const completenessEndRow = completenessStartRow + completeness.length - 1;
summary.getRange(`A${completenessTitleRow + 1}:D${completenessEndRow}`).format = {
  font: { name: "Aptos", size: 10 },
  borders: { preset: "inside", style: "thin", color: "#D9E2F3" },
};
summary.getRange(`A${completenessTitleRow + 1}:D${completenessTitleRow + 1}`).format = {
  fill: "#E2F0D9",
  font: { name: "Aptos", size: 10, bold: true, color: "#1F1F1F" },
  borders: { preset: "inside", style: "thin", color: "#A9D18E" },
};
summary.getRange(`B${completenessStartRow}:D${completenessEndRow}`).format.numberFormat = "0";

summary.getRange("A:H").format.font = { name: "Aptos", size: 10 };
summary.getRange("A1:H45").format.wrapText = false;
summary.getRange("A3:H4").format.wrapText = true;
summary.getRange("A:A").format.columnWidth = 30;
summary.getRange("B:B").format.columnWidth = 21;
summary.getRange("C:C").format.columnWidth = 17;
summary.getRange("D:D").format.columnWidth = 27;
summary.getRange("E:E").format.columnWidth = 18;
summary.getRange("F:F").format.columnWidth = 42;
summary.getRange("G:G").format.columnWidth = 18;
summary.getRange("H:H").format.columnWidth = 18;

raw.showGridLines = false;
raw.freezePanes.freezeRows(1);
raw.freezePanes.freezeColumns(7);
const rawUsed = raw.getUsedRange();
rawUsed.format = {
  font: { name: "Aptos", size: 9 },
  verticalAlignment: "center",
};
raw.getRangeByIndexes(0, 0, 1, headers.length).format = {
  fill: "#1F4E78",
  font: { name: "Aptos", size: 9, bold: true, color: "#FFFFFF" },
  wrapText: true,
  verticalAlignment: "center",
};
raw.getRangeByIndexes(0, 0, 1, headers.length).format.rowHeight = 60;
rawUsed.format.columnWidth = 18;
raw.getRange("A:A").format.columnWidth = 32;
raw.getRange("B:B").format.columnWidth = 14;
raw.getRange("C:C").format.columnWidth = 12;
raw.getRange("D:D").format.columnWidth = 20;
raw.getRange("E:E").format.columnWidth = 29;
raw.getRange("F:F").format.columnWidth = 45;
raw.getRange("G:G").format.columnWidth = 16;
raw.getRange("H:I").format.columnWidth = 18;
raw.getRange("J:J").format.columnWidth = 24;
raw.getRange("K:K").format.columnWidth = 15;
raw.getRange("L:L").format.columnWidth = 22;
raw.getRange("L2:L3").format.numberFormat = "yyyy-mm-dd hh:mm:ss";
raw.getRange("M:M").format.columnWidth = 26;
raw.getRange("A1:DK3").format.borders = { preset: "inside", style: "thin", color: "#D9E2F3" };
const rawTable = raw.tables.add("A1:DK3", true, "ComparisonAnalysisReadyTable");
rawTable.style = "TableStyleMedium2";
rawTable.showBandedRows = true;

await fs.mkdir(outputDir, { recursive: true });
const summaryImage = await workbook.render({ sheetName: "Comparison Summary", range: `A1:H${completenessEndRow}`, scale: 1.4, format: "png" });
await fs.writeFile(previewSummary, new Uint8Array(await summaryImage.arrayBuffer()));
const rawImage = await workbook.render({ sheetName: "Analysis Ready", range: "A1:R3", scale: 1.2, format: "png" });
await fs.writeFile(previewRaw, new Uint8Array(await rawImage.arrayBuffer()));

const keyCheck = await workbook.inspect({
  kind: "table",
  range: `Comparison Summary!A1:H${nasaEndRow}`,
  include: "values,formulas",
  tableMaxRows: 40,
  tableMaxCols: 8,
  maxChars: 9000,
});
console.log(keyCheck.ndjson);

const errors = await workbook.inspect({
  kind: "match",
  searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
  options: { useRegex: true, maxResults: 100 },
  summary: "final formula error scan",
});
console.log(errors.ndjson);

const output = await SpreadsheetFile.exportXlsx(workbook);
await output.save(outputXlsx);
console.log(`Wrote ${outputXlsx}`);
