import sys
import json
from pypdf import PdfReader, PdfWriter

def main():
    # Parametreler: [script] [input_pdf] [range_string] [output_pdf]
    if len(sys.argv) < 4:
        print(json.dumps({"error": "Eksik parametre."}))
        return

    input_pdf = sys.argv[1]
    range_str = sys.argv[2] # Örn: "1,3-5,10"
    output_pdf = sys.argv[3]

    try:
        reader = PdfReader(input_pdf)
        writer = PdfWriter()
        total_pages = len(reader.pages)
        
        # Kullanıcının girdiği "1, 3-5" stringini analiz et
        parts = range_str.split(',')
        selected_count = 0

        for part in parts:
            part = part.strip()
            if '-' in part:
                # Aralık (Örn: 3-5)
                try:
                    start, end = map(int, part.split('-'))
                    # Kullanıcı 1'den başlar, Python 0'dan.
                    # range(start-1, end) -> start-1 dahil, end hariç.
                    # Kullanıcı 3-5 dediyse (3,4,5), range(2, 5) olmalı.
                    for i in range(start - 1, end):
                        if 0 <= i < total_pages:
                            writer.add_page(reader.pages[i])
                            selected_count += 1
                except:
                    pass
            else:
                # Tek Sayfa (Örn: 8)
                if part.isdigit():
                    idx = int(part) - 1
                    if 0 <= idx < total_pages:
                        writer.add_page(reader.pages[idx])
                        selected_count += 1

        if selected_count == 0:
            print(json.dumps({"error": "Seçilen aralıkta sayfa bulunamadı."}))
            return

        with open(output_pdf, "wb") as f:
            writer.write(f)

        print(json.dumps({"status": "success", "message": f"{selected_count} sayfa ayrıldı."}))

    except Exception as e:
        print(json.dumps({"error": str(e)}))

if __name__ == "__main__":
    main()