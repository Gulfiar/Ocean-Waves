const admin = require('firebase-admin');
const fs = require('fs');
const readline = require('readline');
const serviceAccount = require("./serviceAccountKey.json");

// Inisialisasi Firebase
admin.initializeApp({
  credential: admin.credential.cert(serviceAccount)
});

const db = admin.firestore();

const rl = readline.createInterface({
  input: process.stdin,
  output: process.stdout
});

// Helper untuk bertanya di terminal (Promise based)
const ask = (question) => new Promise((resolve) => rl.question(question, resolve));

// Fungsi Parser Teks BMKG
function parseBMKGText(text, loc_name, lat, long, startDateStr) {
  const lines = text.split('\n').map(l => l.trim()).filter(l => l.length > 0);
  let hours = [], temp = [], humid = [], wdir = [], wspd = [], gust = [], wvht = [], cdir = [], cspd = [];

  for (let i = 0; i < lines.length; i++) {
    let line = lines[i];
    if (line.startsWith('Jam')) hours = line.substring(3).trim().split(/\s+/);
    else if (line.startsWith('Suhu Udara')) temp = line.substring(line.indexOf(')') + 1).trim().split(/\s+/);
    else if (line.startsWith('Kelembapan Udara')) humid = line.substring(line.indexOf(')') + 1).trim().split(/\s+/);
    else if (line.startsWith('Arah Angin')) { wdir = lines[i+1].trim().split(/\s+/); i++; }
    else if (line.startsWith('Kecepatan Angin')) wspd = line.substring(line.indexOf(')') + 1).trim().split(/\s+/);
    else if (line.startsWith('Wind Gust')) gust = line.substring(line.indexOf(')') + 1).trim().split(/\s+/);
    else if (line.startsWith('Tinggi Gelombang Signifikan')) wvht = line.substring(line.indexOf(')') + 1).trim().split(/\s+/);
    else if (line.startsWith('Arah Arus Permukaan')) { cdir = lines[i+1].trim().split(/\s+/); i++; }
    else if (line.startsWith('Kecepatan Arus Permukaan')) {
      if (i+1 < lines.length && lines[i+1].includes('(knot)')) {
        cspd = lines[i+2].trim().split(/\s+/);
        i += 2;
      } else if (line.includes('(knot)')) {
        cspd = line.substring(line.indexOf(')') + 1).trim().split(/\s+/);
        if (cspd.length === 0) { cspd = lines[i+1].trim().split(/\s+/); i++; }
      } else if (i+1 < lines.length) {
        cspd = lines[i+1].trim().split(/\s+/);
        i++;
      }
    }
  }

  if (hours.length === 0) throw new Error("Gagal membaca baris 'Jam' dari file teks.");

  let jsonData = {};
  let currentDate = new Date(startDateStr);
  let prevHour = -1;

  for (let j = 0; j < hours.length; j++) {
    let hrStr = hours[j];
    let hr = parseInt(hrStr, 10);
    // Jika jam mundur (misal 23 ke 00), berarti pindah hari
    if (hr < prevHour) currentDate.setDate(currentDate.getDate() + 1);
    prevHour = hr;

    let y = currentDate.getFullYear();
    let m = String(currentDate.getMonth() + 1).padStart(2, '0');
    let d = String(currentDate.getDate()).padStart(2, '0');
    let dateStr = `${y}-${m}-${d}`;
    let docId = `${y}${m}${d}${hrStr}`;

    jsonData[docId] = {
      loc: loc_name,
      lat: parseFloat(lat),
      long: parseFloat(long),
      date: dateStr,
      time: `${hrStr}:00`,
      TEMP: temp[j] || 0,
      HUMID: humid[j] || 0,
      WDIR: wdir[j] || "-",
      WSPD: wspd[j] || 0,
      GST: gust[j] || 0,
      WVHT: wvht[j] || 0,
      CDIR: cdir[j] || "-",
      CSPD: cspd[j] || 0
    };
  }
  return jsonData;
}

async function startImport() {
  try {
    console.log("=== FIRESTORE INTERACTIVE IMPORT TOOL ===");

    // 1. Input Nama File
    let fileName = await ask('1. Masukkan nama file beserta ekstensinya (contoh: data.json atau data.txt): ');
    if (!fs.existsSync(fileName)) {
      throw new Error(`File "${fileName}" tidak ditemukan!`);
    }

    const rawData = fs.readFileSync(fileName, 'utf8');
    let jsonData = {};

    if (fileName.endsWith('.json')) {
      jsonData = JSON.parse(rawData);
    } else if (fileName.endsWith('.txt')) {
      console.log("\n--- Mode Parsing Teks BMKG Aktif ---");
      let loc_name = await ask('Masukkan Nama Lokasi (contoh: Laut Banda): ');
      let latStr = await ask('Masukkan Latitude (contoh: -4.5): ');
      let longStr = await ask('Masukkan Longitude (contoh: 129.5): ');
      let startDateStr = await ask('Masukkan Tanggal Awal Data (Format YYYY-MM-DD, contoh: 2026-05-12): ');
      
      jsonData = parseBMKGText(rawData, loc_name, latStr, longStr, startDateStr);
      console.log(`\nBerhasil mem-parsing ${Object.keys(jsonData).length} jam data dari teks!`);
    } else {
      throw new Error("Format file tidak didukung! Harus .json atau .txt");
    }

    // 2. Input Nama Koleksi
    let collectionName = await ask('\n2. Masukkan nama Koleksi tujuan di Firestore: ');
    if (!collectionName) throw new Error("Nama koleksi tidak boleh kosong!");

    console.log(`\nMemulai proses import ke koleksi: [${collectionName}]...`);

    const batch = db.batch();
    const collectionRef = db.collection(collectionName);

    let count = 0;
    for (const docId in jsonData) {
      const item = jsonData[docId];

      // Transformasi Data sesuai standar Software Engineering Anda
      const transformedData = {
        loc_name: item.loc || item.loc_name || "Unknown",
        posisi: new admin.firestore.GeoPoint(item.lat || 0, item.long || 0),
        date: item.date || "",
        time: item.time || "",
        TEMP: Number(item.TEMP) || 0,
        HUMID: Number(item.HUMID) || 0,
        WDIR: item.WDIR || "-",
        WSPD: Number(item.WSPD) || 0,
        GST: Number(item.GST) || 0,
        WVHT: Number(item.WVHT) || 0,
        CDIR: item.CDIR || "-",
        CSPD: Number(item.CSPD) || 0,
        // Gabungan date & time menjadi format Date objek
        timestamp: admin.firestore.Timestamp.fromDate(new Date(`${item.date}T${item.time}:00`))
      };

      const docRef = collectionRef.doc(docId);
      batch.set(docRef, transformedData);
      count++;
    }

    // 3. Eksekusi Batch
    await batch.commit();
    console.log(`\nBERHASIL!`);
    console.log(`- File: ${fileName}`);
    console.log(`- Koleksi: ${collectionName}`);
    console.log(`- Total: ${count} dokumen telah di-update/buat.`);

  } catch (error) {
    console.error(`\nERROR: ${error.message}`);
  } finally {
    rl.close();
  }
}

startImport();