import { Link, useLocation, useParams } from 'react-router'
import type { PublicBooking } from '@/api/types'
import { BookingSummary } from '@/components/BookingSummary'
import { Alert } from '@/components/ui'

export default function ConfirmationPage() {
  const { reference } = useParams()
  const booking = useLocation().state as PublicBooking | null

  return (
    <div className="stack" style={{ maxWidth: 640 }}>
      <h1>Teşekkürler!</h1>
      <Alert tone="success">
        Rezervasyon numaranız: <strong>{reference}</strong>. Onay bilgileri e-posta adresinize gönderilecektir.
      </Alert>
      {booking && <BookingSummary booking={booking} />}
      <p className="small">Rezervasyonunuzu görüntülemek veya iptal etmek için <Link to="/my-booking">Rezervasyonum</Link> sayfasını kullanabilirsiniz.</p>
    </div>
  )
}
