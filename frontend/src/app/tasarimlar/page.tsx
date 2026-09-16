"use client";

import Image from "next/image";
import { useState } from "react";
import { BrandMark, Icon } from "@/components/icons";
import { AdminGate } from "@/components/ui";
import styles from "./tasarimlar.module.css";

type DesignId = "sahne" | "atolye" | "ritim";

const DESIGNS: { id: DesignId; number: string; title: string; note: string }[] = [
  { id: "sahne", number: "01", title: "Sahne", note: "Sinematik / premium" },
  { id: "atolye", number: "02", title: "Atölye", note: "Editoryal / sıcak" },
  { id: "ritim", number: "03", title: "Ritim", note: "Cesur / kültürel" },
];

const PROGRAMS = [
  { name: "Piyano", age: "6 yaş ve üzeri", icon: "piano" as const },
  { name: "Gitar", age: "8 yaş ve üzeri", icon: "guitar" as const },
  { name: "Keman", age: "6 yaş ve üzeri", icon: "violin" as const },
  { name: "Bateri", age: "8 yaş ve üzeri", icon: "drums" as const },
];

// İç tasarım denemesi - herkese açık bir rotada dummy içerik, nav'dan hiç bağlanmıyor.
// Yalnızca Admin görsün diye kapatıldı (bkz. AdminGate).
export default function DesignLabPage() {
  return <AdminGate><DesignLabPageContent /></AdminGate>;
}

function DesignLabPageContent() {
  const [active, setActive] = useState<DesignId>("sahne");

  return (
    <main className={styles.lab}>
      <header className={styles.labBar}>
        <div className={styles.labIdentity}>
          <BrandMark compact />
          <span><strong>Abdera Web Sitesi</strong><small>3 alternatif · dummy içerik</small></span>
        </div>
        <nav className={styles.designTabs} aria-label="Web sitesi tasarım alternatifleri">
          {DESIGNS.map((design) => (
            <button key={design.id} type="button" aria-pressed={active === design.id} onClick={() => setActive(design.id)} className={active === design.id ? styles.designTabActive : styles.designTab}>
              <span>{design.number}</span><strong>{design.title}</strong><small>{design.note}</small>
            </button>
          ))}
        </nav>
      </header>
      <section className={styles.canvas} aria-live="polite">
        {active === "sahne" && <StageWebsite />}
        {active === "atolye" && <AtelierWebsite />}
        {active === "ritim" && <RhythmWebsite />}
      </section>
    </main>
  );
}

function StageWebsite() {
  return (
    <div className={styles.stageSite}>
      <header className={styles.stageHeader}>
        <BrandMark />
        <nav aria-label="Ana menü"><a href="#stage-about">Biz kimiz?</a><a href="#stage-programs">Eğitimler</a><a href="#stage-event">Etkinlikler</a><a href="#stage-contact">İletişim</a></nav>
        <a href="#stage-contact" className={styles.stageHeaderCta}>Tanışma dersi <Icon name="arrow-right" /></a>
      </header>
      <section className={styles.stageHero}>
        <Image src="/design-assets/abdera-stage.webp" alt="Sıcak sahne ışığında birlikte prova yapan müzisyenler" fill priority sizes="100vw" />
        <div className={styles.stageHeroShade} />
        <div className={styles.stageHeroCopy}>
          <span className={styles.stageEyebrow}>SEFERİHİSAR’DA SANAT EĞİTİMİ</span>
          <h1>Sahne senin.<br /><em>Sesin burada</em> başlıyor.</h1>
          <p>Her yaştan öğrencinin kendi ritmini keşfettiği, birlikte ürettiği ve özgüvenle sahneye çıktığı bir sanat okulu.</p>
          <div className={styles.stageHeroActions}><a href="#stage-programs">Eğitimleri keşfet <Icon name="arrow-right" /></a><a href="#stage-about">Abdera’yı tanı</a></div>
        </div>
        <div className={styles.stageHeroFoot}><span>01 / MÜZİKLE BÜYÜ</span><div><i /><i /><i /></div><small>Aşağı kaydır</small></div>
      </section>
      <section className={styles.stageIntro} id="stage-about">
        <span className={styles.stageSectionNo}>01</span>
        <div><small>ABDERA SANAT AKADEMİ</small><h2>Nota öğretmekten fazlası.</h2></div>
        <div className={styles.stageIntroText}><p>Öğrencilerimizin merakını, disiplinini ve sahne cesaretini aynı anda besleyen kişiye özel bir eğitim yaklaşımı kuruyoruz.</p><a href="#stage-contact">Yaklaşımımızı keşfet <Icon name="arrow-right" /></a></div>
      </section>
      <section className={styles.stageStats} aria-label="Kurum istatistikleri">
        <article><strong>10+</strong><span>yıllık deneyim</span></article><article><strong>150</strong><span>aktif öğrenci</span></article><article><strong>06</strong><span>uzman eğitmen</span></article><article><strong>04</strong><span>performans stüdyosu</span></article>
      </section>
      <section className={styles.stagePrograms} id="stage-programs">
        <header><div><small>EĞİTİMLER</small><h2>Kendi enstrümanını<br />bul, hikâyeni anlat.</h2></div><p>Birebir dersler, sınav hazırlık programları ve sahne çalışmalarıyla öğrencinin temposuna göre ilerleyen eğitimler.</p></header>
        <div className={styles.stageProgramGrid}>{PROGRAMS.map((program, index) => <article key={program.name}><span>0{index + 1}</span><Icon name={program.icon} /><div><h3>{program.name}</h3><small>{program.age}</small></div><button type="button" aria-label={`${program.name} programını incele`}><Icon name="arrow-right" /></button></article>)}</div>
      </section>
      <section className={styles.stageEvent} id="stage-event">
        <div><span>YAKLAŞAN ETKİNLİK</span><strong>21.06</strong><small>Yaz Sahnesi · Sığacık</small></div><blockquote>“Öğrendiğimiz her şeyi bu kez denizin kıyısında, birlikte sahneye taşıyoruz.”</blockquote><a href="#stage-contact">Programı gör <Icon name="arrow-right" /></a>
      </section>
      <footer className={styles.stageFooter} id="stage-contact">
        <div><BrandMark /><p>Sanatla büyüyen, kendini özgürce ifade eden nesiller için.</p></div><div><small>BİZE ULAŞIN</small><strong>merhaba@abdera.example</strong><span>Seferihisar · İzmir</span></div><a href="https://www.instagram.com/abderasanat/">Instagram <Icon name="arrow-right" /></a>
      </footer>
    </div>
  );
}

function AtelierWebsite() {
  return (
    <div className={styles.atelierSite}>
      <header className={styles.atelierHeader}>
        <BrandMark /><nav aria-label="Ana menü"><a href="#atelier-story">Hikâyemiz</a><a href="#atelier-programs">Programlar</a><a href="#atelier-life">Abdera’da yaşam</a></nav><a href="#atelier-contact">İletişim <Icon name="arrow-right" /></a>
      </header>
      <section className={styles.atelierHero}>
        <div className={styles.atelierHeroCopy}><span>TC MEB SEFERİHİSAR KİŞİSEL GELİŞİM VE YETENEK KURSU</span><h1>Sanatla büyümek,<br /><em>kendinle tanışmaktır.</em></h1><p>Müziği yalnızca çalmayı değil, dinlemeyi, hissetmeyi ve paylaşmayı öğreniyoruz.</p><a href="#atelier-programs">Sana uygun programı bul <Icon name="arrow-right" /></a></div>
        <div className={styles.atelierHeroImage}><Image src="/design-assets/abdera-atelier.webp" alt="Gün ışığı alan bir müzik atölyesinde prova" fill priority sizes="(max-width: 800px) 100vw, 54vw" /><div className={styles.atelierStamp}><strong>ABDERA</strong><span>Sanat · Eğitim · Sahne</span></div><small>Seferihisar, 2016’dan beri</small></div>
      </section>
      <section className={styles.atelierManifesto} id="atelier-story"><span>Bizim için iyi bir ders;</span><p>öğrencinin kapıdan girerken taşıdığı merakı, çıkarken <em>özgüvene</em> dönüştürür.</p></section>
      <section className={styles.atelierPrograms} id="atelier-programs">
        <header><span>PROGRAMLAR / 01—04</span><h2>Her öğrencinin<br />ritmi başka.</h2><p>Ders planı öğrencinin yaşı, hedefi ve öğrenme biçimine göre şekillenir.</p></header>
        <div>{PROGRAMS.map((program, index) => <article key={program.name}><span>0{index + 1}</span><Icon name={program.icon} /><h3>{program.name}</h3><p>{index === 0 ? "Klasikten çağdaşa, sağlam bir teknik ve güçlü bir yorum." : index === 1 ? "Akorlardan sahne repertuvarına, çalarak öğren." : index === 2 ? "Doğru duruş, temiz ses ve müzikal ifade." : "Ritim duygusunu bedenden enstrümana taşı."}</p><button type="button">Programı incele <Icon name="arrow-right" /></button></article>)}</div>
      </section>
      <section className={styles.atelierLife} id="atelier-life">
        <div className={styles.atelierLifeImage}><Image src="/design-assets/abdera-stage.webp" alt="Sahne provası" fill sizes="(max-width: 800px) 100vw, 48vw" /></div>
        <div><span>ABDERA’DA YAŞAM</span><h2>Ders biter.<br />Müzik devam eder.</h2><p>Atölye buluşmaları, dönem konserleri ve sınav hazırlık programlarıyla öğrenmeyi sınıfın dışına taşıyoruz.</p><div><strong>Trinity & LCM</strong><small>Sınav hazırlık programları</small></div><div><strong>Yıl sonu sahnesi</strong><small>Tüm öğrenciler için performans deneyimi</small></div></div>
      </section>
      <section className={styles.atelierContact} id="atelier-contact"><small>TANIŞALIM</small><h2>İlk notayı birlikte çalalım.</h2><p>Yaş, deneyim ve hedef bilgini bırak; sana uygun programı birlikte belirleyelim.</p><a href="mailto:merhaba@abdera.example">Tanışma dersi planla <Icon name="arrow-right" /></a></section>
      <footer className={styles.atelierFooter}><BrandMark compact /><span>Seferihisar · İzmir</span><a href="https://www.instagram.com/abderasanat/">@abderasanat</a><small>© 2026 Abdera Sanat Akademi</small></footer>
    </div>
  );
}

function RhythmWebsite() {
  return (
    <div className={styles.rhythmSite}>
      <header className={styles.rhythmHeader}><div><Image src="/abdera-logo.webp" alt="Abdera" width={58} height={58} /><strong>ABDERA</strong></div><nav aria-label="Ana menü"><a href="#rhythm-programs">EĞİTİMLER</a><a href="#rhythm-about">BİZ KİMİZ?</a><a href="#rhythm-contact">İLETİŞİM</a></nav><a href="#rhythm-contact">DERSİNE BAŞLA ↗</a></header>
      <section className={styles.rhythmHero}>
        <div className={styles.rhythmHeroTitle}><span>SEFERİHİSAR / 2026</span><h1>SESİNİ<br /><em>BUL.</em></h1></div>
        <div className={styles.rhythmHeroImage}><Image src="/design-assets/abdera-rhythm.webp" alt="Piyano, gitar, keman ve bateriden oluşan ritmik kolaj" fill priority sizes="(max-width: 800px) 100vw, 52vw" /><span>PLAY<br />LOUD</span></div>
        <div className={styles.rhythmHeroSide}><Icon name="music" /><p>Çal. Dinle.<br />Üret. Paylaş.</p><a href="#rhythm-programs">PROGRAMLARI GÖR <Icon name="arrow-right" /></a></div>
      </section>
      <div className={styles.rhythmTicker}><span>PİYANO</span><i /><span>GİTAR</span><i /><span>KEMAN</span><i /><span>BATERİ</span><i /><span>RESİM</span><i /><span>SAHNE</span></div>
      <section className={styles.rhythmAbout} id="rhythm-about"><span>01 / BİZ KİMİZ?</span><h2>Sadece nota değil,<br /><em>karakter</em> çalışıyoruz.</h2><div><p>Abdera, öğrencilerin kendi sesini keşfettiği bağımsız bir üretim alanı. Teknik, disiplin ve sahne cesareti burada aynı programa dahil.</p><strong>10+ YIL<br />150 ÖĞRENCİ<br />6 EĞİTMEN</strong></div></section>
      <section className={styles.rhythmPrograms} id="rhythm-programs">
        <header><span>02 / EĞİTİMLER</span><h2>HANGİ SES<br />SENİN?</h2></header>
        <div>{PROGRAMS.map((program, index) => <article key={program.name} className={index === 1 ? styles.rhythmProgramHot : undefined}><span>0{index + 1}</span><Icon name={program.icon} /><h3>{program.name}</h3><small>{program.age}</small><button type="button" aria-label={`${program.name} programını aç`}><Icon name="arrow-right" /></button></article>)}</div>
      </section>
      <section className={styles.rhythmEvent}><div><span>21</span><small>HAZ<br />2026</small></div><div><span>YAZ SAHNESİ</span><h2>Sığacık’ta<br />hep birlikte.</h2></div><p>Öğrenci konserleri<br />Açık sahne<br />Atölye buluşmaları</p><button type="button">PROGRAM ↗</button></section>
      <section className={styles.rhythmContact} id="rhythm-contact"><span>03 / TEMAS</span><h2>HAZIRSAN<br /><em>BAŞLAYALIM.</em></h2><a href="mailto:merhaba@abdera.example">MERHABA@ABDERA.EXAMPLE <Icon name="arrow-right" /></a></section>
      <footer className={styles.rhythmFooter}><strong>ABDERA®</strong><span>SEFERİHİSAR / İZMİR</span><a href="https://www.instagram.com/abderasanat/">INSTAGRAM ↗</a><small>©2026</small></footer>
    </div>
  );
}
