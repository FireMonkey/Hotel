import { createBrowserRouter } from 'react-router'
import PublicLayout from '@/layouts/PublicLayout'
import AdminLayout from '@/layouts/AdminLayout'
import HomePage from '@/pages/public/HomePage'
import RoomsPage from '@/pages/public/RoomsPage'
import SearchPage from '@/pages/public/SearchPage'
import BookPage from '@/pages/public/BookPage'
import ConfirmationPage from '@/pages/public/ConfirmationPage'
import MyBookingPage from '@/pages/public/MyBookingPage'
import LoginPage from '@/pages/admin/LoginPage'
import DashboardPage from '@/pages/admin/DashboardPage'
import BookingsPage from '@/pages/admin/BookingsPage'
import BookingDetailPage from '@/pages/admin/BookingDetailPage'
import NewBookingPage from '@/pages/admin/NewBookingPage'
import CalendarPage from '@/pages/admin/CalendarPage'
import RoomsAdminPage from '@/pages/admin/RoomsAdminPage'
import RoomTypesPage from '@/pages/admin/RoomTypesPage'
import ChannelsPage from '@/pages/admin/ChannelsPage'
import ChannelDetailPage from '@/pages/admin/ChannelDetailPage'
import UsersPage from '@/pages/admin/UsersPage'
import NotFoundPage from '@/pages/NotFoundPage'

export const router = createBrowserRouter([
  {
    element: <PublicLayout />,
    children: [
      { index: true, element: <HomePage /> },
      { path: 'rooms', element: <RoomsPage /> },
      { path: 'search', element: <SearchPage /> },
      { path: 'book', element: <BookPage /> },
      { path: 'booking/:reference', element: <ConfirmationPage /> },
      { path: 'my-booking', element: <MyBookingPage /> },
    ],
  },
  { path: 'admin/login', element: <LoginPage /> },
  {
    path: 'admin',
    element: <AdminLayout />,
    children: [
      { index: true, element: <DashboardPage /> },
      { path: 'bookings', element: <BookingsPage /> },
      { path: 'bookings/new', element: <NewBookingPage /> },
      { path: 'bookings/:id', element: <BookingDetailPage /> },
      { path: 'calendar', element: <CalendarPage /> },
      { path: 'rooms', element: <RoomsAdminPage /> },
      { path: 'room-types', element: <RoomTypesPage /> },
      { path: 'channels', element: <ChannelsPage /> },
      { path: 'channels/:id', element: <ChannelDetailPage /> },
      { path: 'users', element: <UsersPage /> },
    ],
  },
  { path: '*', element: <NotFoundPage /> },
])
