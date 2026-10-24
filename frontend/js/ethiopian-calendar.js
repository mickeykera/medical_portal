/**
 * Pure, deterministic Ethiopian Calendar (Amete Mihret) conversion library.
 * Designed for offline execution in browser and Node.js with zero dependencies.
 */
(function(root, factory) {
  if (typeof define === 'function' && define.amd) {
    define([], factory);
  } else if (typeof module === 'object' && module.exports) {
    module.exports = factory();
  } else {
    root.EthiopianCalendar = factory();
  }
}(typeof self !== 'undefined' ? self : this, function() {
  'use strict';

  // DayNumber 0 is Gregorian 0001-01-01.
  // 1970-01-01 is DayNumber 719162.
  const UNIX_EPOCH_DAY = 719162;
  const DAY_NUMBER_OFFSET = 2795;

  const MONTH_NAMES = {
    en: [
      '', 'Meskerem', 'Tikimt', 'Hidar', 'Tahsas', 'Tir', 'Yakatit',
      'Magabit', 'Miyazya', 'Ginbot', 'Sene', 'Hamle', 'Nehase', 'Pagume'
    ],
    am: [
      '', 'መስከረም', 'ጥቅምት', 'ኅዳር', 'ታኅሣሥ', 'ጥር', 'የካቲት',
      'መጋቢት', 'ሚያዝያ', 'ግንቦት', 'ሰኔ', 'ሐምሌ', 'ነሐሴ', 'ጳጉሜ'
    ],
    om: [
      '', 'Birraa', 'Onkololeessa', 'Sadaasa', 'Muddee', 'Amajjii', 'Guraandhala',
      'Bitooteessa', 'Ebla', 'Caamsaa', 'Waxabajjii', 'Adooleessa', 'Hagayya', 'Qaammee'
    ]
  };

  const DAY_NAMES = {
    en: ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'],
    am: ['እሑድ', 'ሰኞ', 'ማክሰኞ', 'ረቡዕ', 'ሐሙስ', 'ዓርብ', 'ቅዳሜ'],
    om: ['Dilbata', 'Wiixata', 'Qibxata', 'Roobii', 'Kamiisa', 'Jimmata', 'Sanbata duraa']
  };

  function gDateToDayNumber(year, month, day) {
    const ms = Date.UTC(year, month - 1, day);
    return Math.floor(ms / 86400000) + UNIX_EPOCH_DAY;
  }

  function dayNumberToGDate(dayNumber) {
    const ms = (dayNumber - UNIX_EPOCH_DAY) * 86400000;
    const d = new Date(ms);
    return {
      year: d.getUTCFullYear(),
      month: d.getUTCMonth() + 1,
      day: d.getUTCDate()
    };
  }

  function toEthiopian(gYear, gMonth, gDay) {
    let dayNum;
    if (gYear instanceof Date) {
      dayNum = gDateToDayNumber(gYear.getFullYear(), gYear.getMonth() + 1, gYear.getDate());
    } else if (typeof gYear === 'string') {
      const parts = gYear.split('-').map(Number);
      dayNum = gDateToDayNumber(parts[0], parts[1], parts[2]);
    } else {
      dayNum = gDateToDayNumber(gYear, gMonth, gDay);
    }

    const days = dayNum - DAY_NUMBER_OFFSET;
    if (days < 0) {
      throw new Error('Dates prior to Ethiopian Era Year 1 are not supported.');
    }

    const cycle = Math.floor(days / 1461);
    const rem = days % 1461;

    let yInCycle, dInYear;
    if (rem < 365) {
      yInCycle = 1;
      dInYear = rem;
    } else if (rem < 730) {
      yInCycle = 2;
      dInYear = rem - 365;
    } else if (rem < 1096) {
      yInCycle = 3; // Leap year
      dInYear = rem - 730;
    } else {
      yInCycle = 4;
      dInYear = rem - 1096;
    }

    const year = cycle * 4 + yInCycle;
    const month = Math.floor(dInYear / 30) + 1;
    const day = (dInYear % 30) + 1;

    const isLeap = (year % 4 === 3);
    const isPagume = (month === 13);
    const isEnkutatash = (month === 1 && day === 1);
    const daysInMonth = isPagume ? (isLeap ? 6 : 5) : 30;

    return {
      year,
      month,
      day,
      isLeapYear: isLeap,
      isPagume,
      isEnkutatash,
      daysInMonth,
      format(lang = 'en') {
        const mNames = MONTH_NAMES[lang] || MONTH_NAMES.en;
        return `${mNames[month]} ${day}, ${year}`;
      },
      toISOString() {
        const mm = String(month).padStart(2, '0');
        const dd = String(day).padStart(2, '0');
        return `${year}-${mm}-${dd}`;
      }
    };
  }

  function toGregorian(year, month, day) {
    if (typeof year === 'object') {
      month = year.month;
      day = year.day;
      year = year.year;
    }

    if (year < 1) throw new Error('Ethiopian year must be >= 1');
    if (month < 1 || month > 13) throw new Error('Ethiopian month must be between 1 and 13');
    const isLeap = (year % 4 === 3);
    const maxDays = (month === 13) ? (isLeap ? 6 : 5) : 30;
    if (day < 1 || day > maxDays) throw new Error(`Invalid day ${day} for month ${month} in year ${year}`);

    const days = (year - 1) * 365 + Math.floor(year / 4) + 30 * (month - 1) + (day - 1);
    const g = dayNumberToGDate(days + DAY_NUMBER_OFFSET);
    return new Date(Date.UTC(g.year, g.month - 1, g.day));
  }

  function getMonthName(month, lang = 'en') {
    const list = MONTH_NAMES[lang] || MONTH_NAMES.en;
    return list[month] || '';
  }

  function getDayName(dayOfWeek, lang = 'en') {
    const list = DAY_NAMES[lang] || DAY_NAMES.en;
    return list[dayOfWeek] || '';
  }

  function getFiscalYear(ethDate) {
    return ethDate.month >= 11 ? ethDate.year + 1 : ethDate.year;
  }

  function getFiscalQuarter(ethDate) {
    switch (ethDate.month) {
      case 11: case 12: case 13: case 1: return 1;
      case 2: case 3: case 4: return 2;
      case 5: case 6: case 7: return 3;
      case 8: case 9: case 10: return 4;
      default: return 1;
    }
  }

  return {
    toEthiopian,
    toGregorian,
    getMonthName,
    getDayName,
    getFiscalYear,
    getFiscalQuarter,
    MONTH_NAMES,
    DAY_NAMES
  };
}));
