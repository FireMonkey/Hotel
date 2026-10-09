import { Link } from 'react-router'

export default function NotFoundPage() {
  return (
    <div className="container site-main stack">
      <h1>Sayfa bulunamadı</h1>
      <Link to="/">Ana sayfaya dön</Link>
    </div>
  )
}
