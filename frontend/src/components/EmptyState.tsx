import { useNavigate } from 'react-router-dom';
import { useT } from '../i18n';

export default function EmptyState() {
  const navigate = useNavigate();
  const t = useT();
  return (
    <div className="flex flex-col items-center justify-center min-h-[60vh] text-slate-400">
      <p className="mb-4">{t('common.emptyState.noData')}</p>
      <button onClick={() => navigate('/app')} className="bg-blue-600 hover:bg-blue-500 text-white font-medium px-4 py-2 rounded-lg transition-colors">
        {t('common.emptyState.import')}
      </button>
    </div>
  );
}
