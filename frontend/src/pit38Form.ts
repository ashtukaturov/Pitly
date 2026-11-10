import type { Pit38FormLayout } from './types';

/** Tax years whose PIT-38 form layout is verified on the backend (Pit38FormLayout.SupportedYears). */
export const VERIFIED_FORM_YEARS = '2021–2025';

/**
 * How texts refer to a PIT-38 field: by its number on the verified form of the tax year ("poz. 30"), or, when no
 * form version is verified for that year, by the field's name as printed on the form. Used as translation
 * parameters, so no dictionary string contains a fixed field number.
 */
export function formRefs(form: Pit38FormLayout | null | undefined): Record<string, string | number> {
  if (form) {
    const poz = (n: number) => `poz. ${n}`;
    return {
      version: form.version,
      inne: `poz. ${form.innePrzychody}-${form.inneKoszty}`,
      innePrzychody: poz(form.innePrzychody),
      inneKoszty: poz(form.inneKoszty),
      dochod: poz(form.dochod),
      dochodCap: `Poz. ${form.dochod}`,
      straty: poz(form.straty),
      podstawa: poz(form.podstawa),
      podatek: poz(form.podatek),
      nalezny: poz(form.podatekNalezny),
      niepobrany: poz(form.zryczaltowanyNiepobrany),
      zrycz: poz(form.zryczaltowany),
      zaplacony: poz(form.zaplacony),
      roznica: poz(form.roznica),
      doZaplaty: poz(form.doZaplaty),
    };
  }

  return {
    version: '?',
    inne: '„Inne przychody”',
    innePrzychody: '„Inne przychody” – przychód',
    inneKoszty: '„Inne przychody” – koszty uzyskania przychodów',
    dochod: '„Dochód”',
    dochodCap: '„Dochód”',
    straty: '„Straty z lat ubiegłych”',
    podstawa: '„Podstawa obliczenia podatku”',
    podatek: '„Podatek od dochodów”',
    nalezny: '„Podatek należny”',
    niepobrany: '„Zryczałtowany podatek niepobrany przez płatnika”',
    zrycz: '„Zryczałtowany podatek od przychodów uzyskanych poza granicami RP”',
    zaplacony: '„Podatek zapłacony za granicą”',
    roznica: '„Różnica między zryczałtowanym podatkiem a podatkiem zapłaconym za granicą”',
    doZaplaty: '„Podatek do zapłaty”',
  };
}
