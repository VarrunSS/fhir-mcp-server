# Synthetic test data

Everything in this folder is **synthetic and invented**. The three patients, their
clinicians, identifiers, results and notes do not describe any real person, and any
resemblance is a coincidence. Every resource carries the tag
`urn:example:tags|synthetic`.

Each file is a FHIR R4 `transaction` bundle in which every entry is
`PUT ResourceType/<fixed-id>`, so loading it twice changes nothing. Load them with
`dotnet run --project tools/FhirMcp.Seed` (see the repo `CLAUDE.md`).

The base64 `data` inside each `DocumentReference` is the source of truth for the note
text; decode it to read the note. Every note is longer than the 500-character search
preview, so `search_document_references` always reports `textTruncated: true`.

Changing an id or a value here means changing `evals/FhirMcp.Evals/cases.json` and
`tests/FhirMcp.Tests/Integration/ToolsAgainstHapiTests.cs` in the same commit.

## Ada Quillfeather: `patient-ada.json`

Female, born 1961-04-12, MRN-1001, id `pat-ada`.

- Conditions: `cond-ada-t2dm`, `cond-ada-htn`, `cond-ada-hld`, `cond-ada-uti` (resolved, abated 2024-02-23)
- Observations: `obs-ada-hba1c-2025-03` (8.1 %), `obs-ada-hba1c-2025-09` (7.6 %), `obs-ada-hba1c-2026-03` (7.2 %),
  `obs-ada-bp-2025-09` (142/88), `obs-ada-bp-2026-03` (138/84), `obs-ada-ldl-2026-03`, `obs-ada-egfr-2026-03`,
  `obs-ada-glucose-2026-03`, `obs-ada-weight-2026-03`, `obs-ada-bmi-2026-03`
- MedicationRequests: `med-ada-metformin`, `med-ada-lisinopril`, `med-ada-atorvastatin`, `med-ada-glipizide` (stopped)
- DocumentReferences: `doc-ada-clinic-2026-03`, `doc-ada-discharge-2024-02`, `doc-ada-phone-2025-06`

## Bram Thistlewood: `patient-bram.json`

Male, born 1978-09-30, MRN-2002, id `pat-bram`.

- Conditions: `cond-bram-asthma`, `cond-bram-pneumonia` (resolved, abated 2025-03-04), `cond-bram-rhinitis`
  (seasonal allergic rhinitis, a Condition and not an allergy record), `cond-bram-reflux`
- Observations: `obs-bram-crp-2025-01-14` (142 mg/L), `obs-bram-crp-2025-01-21` (48), `obs-bram-crp-2025-02-18` (4),
  `obs-bram-spo2-2025-01-14`, `obs-bram-spo2-2026-05-06`, `obs-bram-peakflow-2026-05-06`, `obs-bram-bp-2025-01-14`,
  `obs-bram-bp-2026-05-06`, `obs-bram-eos-2026-05-06`, `obs-bram-smoking-2026-05-06` (never smoker)
- MedicationRequests: `med-bram-salbutamol`, `med-bram-budesonide-formoterol`, `med-bram-fluticasone-nasal`,
  `med-bram-clarithromycin` (stopped), `med-bram-doxycycline` (completed)
- DocumentReferences: `doc-bram-discharge-2025-01`, `doc-bram-followup-2025-03`, `doc-bram-clinic-2026-05`

## Chidi Marrowby: `patient-chidi.json`

Male, born 1949-01-22, MRN-3003, id `pat-chidi`.

- Conditions: `cond-chidi-af`, `cond-chidi-ckd3`, `cond-chidi-oa`, `cond-chidi-cellulitis` (resolved, abated 2023-09-18)
- Observations: `obs-chidi-egfr-2024-09` (52), `obs-chidi-egfr-2025-09` (47), `obs-chidi-egfr-2026-08` (44),
  `obs-chidi-inr-2026-06` (2.4), `obs-chidi-inr-2026-07` (3.1), `obs-chidi-inr-2026-08` (2.6),
  `obs-chidi-creatinine-2026-08`, `obs-chidi-bp-2025-09` (150/90), `obs-chidi-bp-2026-08` (146/88),
  `obs-chidi-hr-2026-08`, `obs-chidi-alcohol-2026-08`
- MedicationRequests: `med-chidi-warfarin`, `med-chidi-bisoprolol`, `med-chidi-paracetamol`,
  `med-chidi-naproxen` (stopped)
- DocumentReferences: `doc-chidi-clinic-2026-08`, `doc-chidi-anticoag-2026-07`, `doc-chidi-discharge-2023-09`

## Facts that appear only in a note

None of these is recorded in any structured resource. An agent that never reads the
notes cannot answer them.

| Note | Fact |
| --- | --- |
| `doc-ada-clinic-2026-03` | Tingling in both feet for about two months; father had a heart attack at 54; sister has type 2 diabetes; plan to repeat HbA1c and a fasting lipid panel on 2026-09-15; podiatry foot check within two months |
| `doc-ada-phone-2025-06` | Why glipizide was stopped: three early-morning episodes of sweating and shakiness in six weeks, two meter readings below 3.5 mmol/L, after the evening dose was increased; stopped on 2025-06-24 (the structured stop reason is only "Adverse effect") |
| `doc-ada-discharge-2024-02` | Admission 2024-02-06 to 2024-02-09 for a urinary tract infection with right flank pain; two days of intravenous antibiotics, seven days in total; repeat urine test two weeks later |
| `doc-bram-discharge-2025-01` | Clarithromycin stopped after three days for persistent nausea and poor appetite, switched to doxycycline; chest x-ray showed right lower lobe consolidation; repeat x-ray in six weeks |
| `doc-bram-followup-2025-03` | Repeat chest x-ray showed the right lower lobe cleared; still coughs when cycling in cold air; wants to build up cycling to work gradually |
| `doc-bram-clinic-2026-05` | Night-time dry cough about twice a week and reliever about three times a week; mother has asthma and younger brother has eczema; hay fever flares each May and June; spirometry booked for 2026-11-10; four-week night-symptom diary |
| `doc-chidi-clinic-2026-08` | Evening palpitations about twice a week lasting a few minutes; brother had a stroke at 70; daughter fills his weekly pill organiser; plan to recheck kidney function and electrolytes on 2026-11-23 |
| `doc-chidi-anticoag-2026-07` | The INR of 3.1 followed more leafy greens and a few days of poor appetite after a stomach upset; omit half a tablet on one Saturday and recheck the INR in a week; carries an anticoagulant alert card |
| `doc-chidi-discharge-2023-09` | Admission 2023-08-28 to 2023-09-01 for cellulitis of the left lower leg; INR checked daily during the stay; walks with a stick; a further week of oral antibiotics |

## Deliberately absent facts

These are missing on purpose, so that questions about them have the answer
"not in the record". Do not add them, in structured data or in a note.

- No patient has an `AllergyIntolerance` resource, and no note mentions allergies or
  reactions. (Bram's seasonal allergic rhinitis is a diagnosis, not an allergy record.)
- No smoking status for Ada: no social-history smoking `Observation` and no mention in any note.
  (Bram has a smoking-status observation; Chidi has an alcohol-use observation.)
- No HbA1c for Chidi, and no mention of diabetes or blood glucose in his record.
- No body weight or BMI for Bram, in structured data or in a note.
- No immunisation information for anyone: no `Immunization` resource and no mention in any note.
