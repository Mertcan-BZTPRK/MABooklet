import sys
import asyncio
import json
import edge_tts
import os

async def main():
    # 1. MOD: SES LİSTESİNİ GETİR
    # Eğer argümanlarda --list varsa, diğerlerine bakmadan listeyi ver ve çık.
    if len(sys.argv) > 1 and sys.argv[1] == "--list":
        try:
            voices = await edge_tts.list_voices()
            print(json.dumps(voices))
        except Exception as e:
            # Hata olursa boş JSON veya hata mesajı dön
            print(json.dumps({"error": str(e)}))
        return

    # 2. MOD: TTS OKUMA İŞLEMİ
    # Eğer liste istenmediyse, TTS için gerekli 4 parametre var mı bak.
    if len(sys.argv) < 5:
        print("Hata: Eksik parametre. Kullanım: tts.exe [Dosya] [Ses] [Hız] [Çıktı]")
        return

    try:
        text_file = sys.argv[1]
        voice = sys.argv[2]
        rate = sys.argv[3]
        output_file = sys.argv[4]
        json_output_file = output_file + ".json"

        # Metni utf-8-sig ile oku (BOM karakteri varsa temizle)
        with open(text_file, "r", encoding="utf-8-sig") as f:
            text = f.read()

        communicate = edge_tts.Communicate(text, voice, rate=rate)
        
        word_boundaries = []

        with open(output_file, "wb") as audio_file:
            async for chunk in communicate.stream():
                if chunk["type"] == "audio":
                    audio_file.write(chunk["data"])
                elif chunk["type"] == "WordBoundary":
                    data = {
                        "offset": chunk["offset"],
                        "length": chunk["length"],
                        "time_ms": chunk["audio_offset"] / 10000.0
                    }
                    word_boundaries.append(data)

        with open(json_output_file, "w", encoding="utf-8") as jf:
            json.dump(word_boundaries, jf, ensure_ascii=False)
            jf.flush()
            os.fsync(jf.fileno())
        
        print("OK")

    except Exception as e:
        print(f"ERROR: {str(e)}")

if __name__ == "__main__":
    # Tüm mantık kontrolünü main içine aldık, burası sadece çalıştırıcı.
    asyncio.run(main())