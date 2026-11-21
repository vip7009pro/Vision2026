---
name: mitsubishi-fx5u-ladder
description: Create Mitsubishi FX5U GX Works3 ladder programs and importable Ladder/Device Comment CSV files with validated structure, encoding, device mapping, and comments.
metadata:
  short-description: Build FX5U Ladder and GX Works3 CSV imports
---

# Mitsubishi FX5U Ladder

Use this skill when the user asks for a Mitsubishi FX5U/FX5U 32MT/ES ladder program, GX Works3 import files, or device comments.

## Scope and deliverables

- Target CPU: `FX5CPU FX5U`, unless the user specifies another FX5 variant.
- Default language: Ladder/listed instruction format.
- Produce separate files for separate import functions:
  - Ladder program: `*.csv` for `Edit -> Import File` in a Ladder editor.
  - Device comments: `*.csv` imported from `Device -> Device Comment`.
- A `.il` file is only a human-readable instruction-list reference; it is not the GX Works3 Ladder import format.
- Do not claim a `.gx3` is complete unless it has been opened/validated in GX Works3 or is based on a verified project template.

## Ladder CSV format

GX Works3 Ladder import CSV must follow the structure exported by GX Works3:

1. First row: project name, one quoted field.
2. Second row: two quoted tab-delimited fields:
   - `Module Type Information:`
   - `FX5CPU FX5U`
3. Third row: exactly these seven quoted tab-delimited headers:
   - `Step No.`
   - `Line Statement`
   - `Instruction`
   - `I/O (Device)`
   - `Blank`
   - `P/I Statement`
   - `Note`
4. Instruction rows contain the instruction in column 3 and operand/device in column 4.
5. End the listed instructions with an `END` row.
6. Leave `Note` blank unless the Note syntax was copied from an actual GX Works3 export. Arbitrary comments in this column can cause `The ladder configuration is invalid`.
7. Use numeric step numbers compatible with the source export. For a simple FX5U sequence, `0, 2, 4, ...` is a practical convention, but preserve the exact numbering when transforming an exported file.

Example structure:

```text
"FX5U_32MT_Test"
"Module Type Information:"\t"FX5CPU FX5U"
"Step No."\t"Line Statement"\t"Instruction"\t"I/O (Device)"\t"Blank"\t"P/I Statement"\t"Note"
"0"\t""\t"LD"\t"X0"\t""\t""\t""
"2"\t""\t"OUT"\t"Y0"\t""\t""\t""
"4"\t""\t"END"\t""\t""\t""\t""
```

For the common Start/Stop latch, use a sequence such as:

```text
LD X0
OR M0
ANI X1
OUT M0
LD M0
OUT Y0
END
```

## Device Comment CSV format

Device comments use a different CSV schema and must not be mixed with Ladder CSV.

1. First row: project name, one quoted field.
2. Second row: the exact headers exported by the target GX Works3. For the supplied FX5U project these are:
   - `Device Name`
   - `English`
3. Each following row has the device name and comment, quoted and separated by a Tab.
4. Keep device names in Mitsubishi notation: `X0`, `X1`, `M0`, `Y0`.
5. Keep comments within the configured device-comment character limit.

Example:

```text
"FX5U_32MT_Test"
"Device Name"\t"English"
"M0"\t"RUN latch"
"X0"\t"Start"
"X1"\t"Stop"
"Y0"\t"Motor output"
```

The header is project/language dependent. If a user supplies an exported comment CSV, copy its first two rows and column order exactly; do not substitute `Device`/`Comment` headers.

## Encoding and delimiters

- Write CSV as Unicode UTF-16 Little Endian with BOM.
- Use Tab delimiters, not commas.
- Quote every field, including blank fields.
- Use CRLF line endings.
- Do not add Markdown, comments, or extra columns to import CSV files.

## Validation workflow

1. Inspect a user-provided GX Works3 export whenever available; it is the authoritative schema for that installation/version/language.
2. Generate Ladder and Device Comment files separately.
3. Verify the first rows, column count, tabs, quotes, encoding, and final `END` instruction.
4. For Ladder import, open the Ladder editor and use `Edit -> Import File`; select `CSV (tab delimited)` and convert the imported unconverted program.
5. For comments, right-click the target Device Comment under `Device -> Device Comment` and choose `Import File`.
6. If GX Works3 reports an error, compare the generated file byte-for-byte in structure with a fresh GX Works3 export; do not guess a new header.

## Important distinction

The Ladder `Note` column is structured ladder metadata, not a general-purpose comment column. Put ordinary explanations in the deliverable documentation or add them later through GX Works3's Device/Label Comment or Statement editor. Keep the Ladder CSV Note column blank unless reproducing a known valid export pattern.
