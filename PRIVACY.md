# Butchi Privacy Policy

Last updated: 23 August 2026

Butchi is a Windows desktop utility for translating and rewriting text with a local GGUF language model.

## Text processing

Text selected or entered in Butchi is processed locally on the user's device by the configured GGUF model. Butchi does not send selected text, prompts, translation results, rewrite results, or history to a cloud AI service.

## Local history

If history is enabled, Butchi stores source text and generated results locally on the user's device. Users can disable history, clear individual history entries, clear all history, or use **Delete history + downloaded models** in Settings.

## Model downloads

Butchi can download optional GGUF model files from Hugging Face when the user explicitly selects **Download**. This network request is used to obtain the selected model file. Selected text and history are not included in the model-download request.

After a model has been downloaded, normal translation and rewrite inference can run locally without contacting a cloud AI service.

## Clipboard and accessibility

Butchi may use Windows UI Automation and clipboard APIs to capture or replace text selected by the user. Clipboard fallback is used only when needed for the requested text action. Butchi does not transmit clipboard content to an external AI service.

## Data deletion

Users can delete local history from Settings. **Delete history + downloaded models** removes Butchi's stored history and downloaded GGUF model files while keeping preferences.

Uninstalling Butchi removes the application. Windows may retain user-created application data depending on installer and operating-system behavior; users can clear local data from Settings before uninstalling.

## Accounts, analytics, and advertising

Butchi does not require an account. Butchi does not include advertising or behavioral tracking and does not intentionally collect analytics or telemetry.

## Third-party services

Hugging Face is used only when downloading a model selected by the user. Hugging Face may process standard network metadata according to its own policies.

## Changes

This policy may be updated when Butchi's data practices change. Material changes will be reflected in this document and in the application/release information where appropriate.

## Contact and support

Project and support: https://github.com/dhhieu113pro/butchi
