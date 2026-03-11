import sys
import os
import json
import hashlib
import google.generativeai as genai
from google.generativeai.types import HarmCategory, HarmBlockThreshold
from pypdf import PdfReader

# --- API KEY ---
# Set the GEMINI_API_KEY environment variable before running
API_KEY = os.environ.get("GEMINI_API_KEY", "")

def get_cached_text_path(pdf_path):
    base_name = os.path.basename(pdf_path)
    file_hash = hashlib.md5(base_name.encode()).hexdigest()
    temp_dir = os.environ.get('TEMP', os.getcwd())
    return os.path.join(temp_dir, f"mabooklet_cache_{file_hash}.txt")

def get_pdf_text(pdf_path):
    txt_path = get_cached_text_path(pdf_path)
    
    # Cache varsa oku
    if os.path.exists(txt_path):
        try:
            with open(txt_path, "r", encoding="utf-8") as f:
                return f.read()
        except:
            pass

    # Yoksa PDF'ten oku
    try:
        reader = PdfReader(pdf_path)
        text = ""
        for page in reader.pages:
            extracted = page.extract_text()
            if extracted:
                text += extracted + "\n"
        
        # --- KRİTİK KONTROL: PDF BOŞ MU? ---
        if len(text.strip()) < 10:
            return None # PDF okunmadı veya resim ağırlıklı

        with open(txt_path, "w", encoding="utf-8") as f:
            f.write(text)
        return text
    except Exception as e:
        return None

def main():
    if len(sys.argv) < 4:
        print(json.dumps({"error": "Eksik parametre."}))
        return

    pdf_path = sys.argv[1]
    user_question = sys.argv[2]
    detail_level = sys.argv[3]

    # 1. PDF OKUMA KONTROLÜ
    context_text = get_pdf_text(pdf_path)
    
    if not context_text:
        # Eğer buraya düşerse suçlu Python kütüphanesidir
        print(json.dumps({"answer": "⚠️ HATA: PDF dosyasından metin okunamadı! Dosya taranmış resimlerden (fotoğraf) oluşuyor olabilir. Bu sistem sadece 'metin' içeren PDF'leri okuyabilir."}))
        return

    genai.configure(api_key=API_KEY)
    
    # Model: Eğer flash-latest hata verirse 'gemini-1.5-flash' denenebilir
    model = genai.GenerativeModel('models/gemini-flash-latest')

    # Güvenlik Ayarları (Filtreleri Kapat)
    safety_settings = {
        HarmCategory.HARM_CATEGORY_HARASSMENT: HarmBlockThreshold.BLOCK_NONE,
        HarmCategory.HARM_CATEGORY_HATE_SPEECH: HarmBlockThreshold.BLOCK_NONE,
        HarmCategory.HARM_CATEGORY_SEXUALLY_EXPLICIT: HarmBlockThreshold.BLOCK_NONE,
        HarmCategory.HARM_CATEGORY_DANGEROUS_CONTENT: HarmBlockThreshold.BLOCK_NONE,
    }

    style = "**Kısa**, net ve özet." if detail_level == "short" else "**Detaylı**, örnekli ve kapsamlı."
    
    prompt = f"""
    GÖREV: Verilen DERS NOTLARI'nı kullanarak soruyu cevapla.
    STİL: {style}
    DİL: Türkçe.
    
    ÖNEMLİ:
    1. Eğer sorunun cevabı notlarda yoksa, "[Notlarda Bulunamadı]" diyip kendi bilgini kullan.
    2. Cevabı Markdown formatında (kalın, başlık, madde) ver.
    3. Telif hakkı uyarısı verip susma, içeriği özetleyerek (kendi cümlelerinle) anlat.
    
    SORU: {user_question}
    
    DERS NOTLARI (İlk 30.000 Karakter):
    {context_text[:30000]}
    """

    try:
        response = model.generate_content(prompt, safety_settings=safety_settings)
        
        # --- DETAYLI HATA ANALİZİ ---
        if response.text:
            print(json.dumps({"answer": response.text}))
        else:
            # Metin yoksa sebebini bul
            feedback = str(response.prompt_feedback) if response.prompt_feedback else "Bilinmiyor"
            reason = str(response.candidates[0].finish_reason) if response.candidates else "Aday Yok"
            
            err_msg = f"⚠️ AI Cevap Vermedi.\nSebep: {reason}\nGeri Bildirim: {feedback}"
            print(json.dumps({"answer": err_msg}))

    except Exception as e:
        err_msg = str(e)
        if "429" in err_msg:
             print(json.dumps({"answer": "⏳ Kota Doldu: Google 'Biraz yavaş ol' diyor. 1-2 dakika bekleyip tekrar dene."}))
        elif "finish_reason" in err_msg or "Quick accessor" in err_msg:
             # Güvenlik veya Telif hatası
             print(json.dumps({"answer": "⚠️ İçerik Engellendi: Google bu soruyu 'Güvenlik' veya 'Telif' gerekçesiyle sansürledi. Soruyu daha basit sormayı dene."}))
        else:
             print(json.dumps({"answer": f"Sistem Hatası: {err_msg}"}))

if __name__ == "__main__":
    main()