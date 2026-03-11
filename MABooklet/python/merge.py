import sys
import os
import json
from pypdf import PdfWriter

def main():
    # Beklenen: [script] [output_pdf_path] [input_pdf_1] [input_pdf_2] ...
    if len(sys.argv) < 3:
        print(json.dumps({"error": "Eksik parametre. En az 2 dosya lazım."}))
        return

    output_path = sys.argv[1]
    input_files = sys.argv[2:]

    merger = PdfWriter()

    try:
        for pdf in input_files:
            merger.append(pdf)

        merger.write(output_path)
        merger.close()
        print(json.dumps({"status": "success", "message": "Dosyalar birleştirildi."}))

    except Exception as e:
        print(json.dumps({"error": str(e)}))

if __name__ == "__main__":
    main()