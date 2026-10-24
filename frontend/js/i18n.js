/**
 * Client-side Internationalization (i18n) Engine for Medical Portal.
 * Supports English (en), Amharic (am), and Afaan Oromoo (om).
 * Works 100% offline with zero external runtime CDN dependencies.
 */
(function(root) {
  'use strict';

  const SUPPORTED_LANGUAGES = [
    { code: 'en', name: 'English', nativeName: 'English', dir: 'ltr' },
    { code: 'am', name: 'Amharic', nativeName: 'አማርኛ', dir: 'ltr' },
    { code: 'om', name: 'Afaan Oromoo', nativeName: 'Afaan Oromoo', dir: 'ltr' }
  ];

  let currentLang = 'en';
  const catalogs = {};
  const listeners = [];

  async function loadCatalog(lang) {
    if (catalogs[lang]) return catalogs[lang];
    try {
      const res = await fetch(`/locales/${lang}.json`);
      if (res.ok) {
        catalogs[lang] = await res.json();
        return catalogs[lang];
      }
    } catch (e) {
      console.warn(`Could not load /locales/${lang}.json, falling back to embedded or API.`);
    }

    try {
      const apiRes = await fetch(`/api/localization/${lang}`);
      if (apiRes.ok) {
        const data = await apiRes.json();
        catalogs[lang] = data.strings || {};
        return catalogs[lang];
      }
    } catch (e) {
      console.error(`Failed to load localization for ${lang}:`, e);
    }

    catalogs[lang] = {};
    return catalogs[lang];
  }

  async function init() {
    const saved = localStorage.getItem('portal_language');
    const browser = (navigator.language || 'en').slice(0, 2).toLowerCase();
    const candidate = saved || (SUPPORTED_LANGUAGES.some(l => l.code === browser) ? browser : 'en');
    await setLanguage(candidate);
  }

  async function setLanguage(lang) {
    const valid = SUPPORTED_LANGUAGES.some(l => l.code === lang) ? lang : 'en';
    currentLang = valid;
    localStorage.setItem('portal_language', valid);
    document.documentElement.lang = valid;

    await loadCatalog(valid);
    if (valid !== 'en' && !catalogs.en) {
      await loadCatalog('en'); // Preload English for fallbacks
    }

    listeners.forEach(fn => {
      try { fn(valid); } catch (e) { console.error('i18n listener error:', e); }
    });

    window.dispatchEvent(new CustomEvent('languageChanged', { detail: { language: valid } }));
  }

  function t(key, params) {
    let str = (catalogs[currentLang] && catalogs[currentLang][key]) ||
              (catalogs.en && catalogs.en[key]) ||
              key;

    if (params && typeof params === 'object') {
      Object.keys(params).forEach(k => {
        str = str.replace(new RegExp(`{${k}}`, 'g'), params[k]);
      });
    }

    return str;
  }

  function onLanguageChanged(fn) {
    if (typeof fn === 'function') listeners.push(fn);
  }

  root.i18n = {
    init,
    setLanguage,
    getLanguage: () => currentLang,
    getSupportedLanguages: () => SUPPORTED_LANGUAGES,
    t,
    onLanguageChanged
  };

  // Auto-init on script load
  if (typeof document !== 'undefined') {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', init);
    } else {
      init();
    }
  }
})(typeof self !== 'undefined' ? self : this);
